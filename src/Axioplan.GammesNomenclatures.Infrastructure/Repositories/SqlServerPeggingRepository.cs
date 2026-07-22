using Axioplan.GammesNomenclatures.Application.Abstractions;
using Axioplan.GammesNomenclatures.Application.Models;
using Axioplan.GammesNomenclatures.Domain;
using Axioplan.GammesNomenclatures.Infrastructure.Data;
using Axioplan.GammesNomenclatures.Infrastructure.Logging;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Axioplan.GammesNomenclatures.Infrastructure.Repositories;

public sealed class SqlServerPeggingRepository(
    IOptions<DatabaseOptions> options,
    ILogger<SqlServerPeggingRepository> logger,
    SqlApplicationLogger sqlLogger) : IPeggingRepository
{
    public async Task<PeggingRunResult> RunPeggingAsync(PeggingRunRequest request, CancellationToken cancellationToken = default)
    {
        await using var connection = OpenConnection();

        var cbnExists = await CbnRunExistsAsync(connection, request.CbnRunId, cancellationToken);
        if (!cbnExists)
        {
            throw new InvalidOperationException($"Run CBN introuvable : {request.CbnRunId}");
        }

        var bidirectional = await LoadBidirectionalFlagAsync(connection, cancellationToken);
        var needs = await LoadMaterialNeedsAsync(connection, request.CbnRunId, cancellationToken);
        var salesLines = await LoadSalesLineSuppliesAsync(connection, request.CbnRunId, cancellationToken);
        var mfgOrders = await LoadManufacturingSuppliesAsync(connection, cancellationToken);
        var purchaseLines = await LoadPurchaseSuppliesAsync(connection, cancellationToken);
        var stocks = await LoadStockSuppliesAsync(connection, cancellationToken);

        var drafts = PeggingEngine.BuildLinks(
            needs, salesLines, mfgOrders, purchaseLines, stocks, bidirectional);

        var versionNo = await GetNextPeggingVersionAsync(connection, request.CbnRunId, cancellationToken);
        var peggingRunId = await InsertPeggingRunAsync(connection, request.CbnRunId, versionNo, cancellationToken);
        foreach (var draft in drafts)
        {
            var linkId = await InsertLinkAsync(connection, peggingRunId, draft, cancellationToken);
            await InsertLinkVersionAsync(connection, linkId, 1, draft.Quantity, "Creation pegging", cancellationToken);
        }

        var links = await LoadLinksForRunAsync(connection, peggingRunId, cancellationToken);
        var coverage = await BuildCoverageForRunAsync(connection, peggingRunId, request.CbnRunId, cancellationToken);
        var supplyUsage = await BuildSupplyUsageForRunAsync(
            connection, peggingRunId, salesLines, mfgOrders, purchaseLines, stocks, cancellationToken);
        var history = await LoadHistoryForRunAsync(connection, peggingRunId, cancellationToken);

        await sqlLogger.LogAsync("INFO", "Pegging", $"Pegging run {peggingRunId} cree pour CBN {request.CbnRunId}", $"{links.Count} liens", cancellationToken);
        logger.LogInformation("Pegging run {PeggingRunId} : {LinkCount} liens pour CBN {CbnRunId}", peggingRunId, links.Count, request.CbnRunId);

        return new PeggingRunResult
        {
            PeggingRunId = peggingRunId,
            CbnRunId = request.CbnRunId,
            VersionNo = versionNo,
            Status = "COMPLETED",
            Links = links,
            Coverage = coverage,
            SupplyUsage = supplyUsage,
            History = history,
        };
    }

    public async Task<IReadOnlyList<PeggingLinkRow>> GetLinksForCbnRunAsync(int cbnRunId, CancellationToken cancellationToken = default)
    {
        await using var connection = OpenConnection();
        var latestRunId = await LoadLatestPeggingRunIdAsync(connection, cbnRunId, cancellationToken);
        return latestRunId is null
            ? []
            : await LoadLinksForRunAsync(connection, latestRunId.Value, cancellationToken);
    }

    public async Task<IReadOnlyList<PeggingCoverageRow>> GetCoverageForCbnRunAsync(int cbnRunId, CancellationToken cancellationToken = default)
    {
        await using var connection = OpenConnection();
        var latestRunId = await LoadLatestPeggingRunIdAsync(connection, cbnRunId, cancellationToken);
        return latestRunId is null
            ? []
            : await BuildCoverageForRunAsync(connection, latestRunId.Value, cbnRunId, cancellationToken);
    }

    public async Task<IReadOnlyList<PeggingSupplyUsageRow>> GetSupplyUsageForCbnRunAsync(int cbnRunId, CancellationToken cancellationToken = default)
    {
        await using var connection = OpenConnection();
        var latestRunId = await LoadLatestPeggingRunIdAsync(connection, cbnRunId, cancellationToken);
        if (latestRunId is null)
        {
            return [];
        }

        var salesLines = await LoadSalesLineSuppliesAsync(connection, cbnRunId, cancellationToken);
        var mfgOrders = await LoadManufacturingSuppliesAsync(connection, cancellationToken);
        var purchaseLines = await LoadPurchaseSuppliesAsync(connection, cancellationToken);
        var stocks = await LoadStockSuppliesAsync(connection, cancellationToken);
        return await BuildSupplyUsageForRunAsync(connection, latestRunId.Value, salesLines, mfgOrders, purchaseLines, stocks, cancellationToken);
    }

    public async Task<IReadOnlyList<PeggingLinkHistoryRow>> GetHistoryForCbnRunAsync(int cbnRunId, CancellationToken cancellationToken = default)
    {
        await using var connection = OpenConnection();
        return await LoadHistoryForCbnRunAsync(connection, cbnRunId, cancellationToken);
    }

    private SqlConnection OpenConnection()
    {
        var connectionString = options.Value.ConnectionString;
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("Chaine de connexion SQL Server introuvable.");
        }

        var connection = new SqlConnection(connectionString);
        connection.Open();
        return connection;
    }

    private static async Task<bool> CbnRunExistsAsync(SqlConnection connection, int cbnRunId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM cbn_runs WHERE id = @id";
        command.Parameters.AddWithValue("@id", cbnRunId);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) > 0;
    }

    private static async Task<bool> LoadBidirectionalFlagAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT pv.value_text
            FROM mvp_parameters p
            JOIN mvp_parameter_values pv ON pv.parameter_id = p.id AND pv.scope_key IS NULL
            WHERE p.code = 'PEGGING_BIDIRECTIONAL_LINKS'
            """;
        var result = await command.ExecuteScalarAsync(cancellationToken) as string;
        return string.Equals(result, "true", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<List<PeggingEngine.MaterialNeed>> LoadMaterialNeedsAsync(
        SqlConnection connection, int cbnRunId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT cmr.id, cmr.sales_order_line_id, cmr.component_article_id, a.code, a.article_type,
                   cmr.quantity_gross, cmr.unit, cmr.variant_label
            FROM cbn_material_requirements cmr
            JOIN articles a ON a.id = cmr.component_article_id
            WHERE cmr.cbn_run_id = @cbnRunId
            """;
        command.Parameters.AddWithValue("@cbnRunId", cbnRunId);

        var needs = new List<PeggingEngine.MaterialNeed>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            needs.Add(new PeggingEngine.MaterialNeed(
                reader.GetInt32(0),
                reader.IsDBNull(1) ? null : reader.GetInt32(1),
                reader.GetInt32(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetDouble(5),
                reader.GetString(6),
                reader.GetString(7)));
        }

        return needs;
    }

    private static async Task<List<PeggingEngine.SupplyCandidate>> LoadSalesLineSuppliesAsync(
        SqlConnection connection, int cbnRunId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT sol.id, so.code + ' L' + CAST(sol.line_no AS NVARCHAR(10)),
                   sol.article_id, sol.quantity, sol.unit
            FROM sales_order_lines sol
            JOIN sales_orders so ON so.id = sol.sales_order_id
            JOIN cbn_runs cr ON cr.sales_order_id = so.id
            WHERE cr.id = @cbnRunId
            """;
        command.Parameters.AddWithValue("@cbnRunId", cbnRunId);

        return await ReadSuppliesAsync(command, "SALES_ORDER_LINE", cancellationToken);
    }

    private static async Task<List<PeggingEngine.SupplyCandidate>> LoadManufacturingSuppliesAsync(
        SqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT mo.id, mo.code, mo.article_id, mo.quantity, mo.unit
            FROM manufacturing_orders mo
            WHERE mo.status IN ('PLANNED', 'RELEASED')
            """;

        return await ReadSuppliesAsync(command, "MANUFACTURING_ORDER", cancellationToken);
    }

    private static async Task<List<PeggingEngine.SupplyCandidate>> LoadPurchaseSuppliesAsync(
        SqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT pol.id, po.code + ' L' + CAST(pol.line_no AS NVARCHAR(10)),
                   pol.article_id, pol.quantity, pol.unit
            FROM purchase_order_lines pol
            JOIN purchase_orders po ON po.id = pol.purchase_order_id
            WHERE po.status = 'OPEN'
            """;

        return await ReadSuppliesAsync(command, "PURCHASE_ORDER_LINE", cancellationToken);
    }

    private static async Task<List<PeggingEngine.SupplyCandidate>> LoadStockSuppliesAsync(
        SqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT sb.id, a.code, sb.article_id, sb.quantity_available, sb.unit
            FROM stock_balances sb
            JOIN articles a ON a.id = sb.article_id
            WHERE sb.quantity_available > 0
            """;

        return await ReadSuppliesAsync(command, "STOCK_BALANCE", cancellationToken);
    }

    private static async Task<List<PeggingEngine.SupplyCandidate>> ReadSuppliesAsync(
        SqlCommand command, string entityType, CancellationToken cancellationToken)
    {
        var supplies = new List<PeggingEngine.SupplyCandidate>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            supplies.Add(new PeggingEngine.SupplyCandidate(
                entityType,
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetInt32(2),
                reader.GetDouble(3),
                reader.GetString(4)));
        }

        return supplies;
    }

    private static async Task<int?> LoadLatestPeggingRunIdAsync(
        SqlConnection connection,
        int cbnRunId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT TOP 1 id
            FROM pegging_runs
            WHERE cbn_run_id = @cbnRunId
            ORDER BY version_no DESC, id DESC
            """;
        command.Parameters.AddWithValue("@cbnRunId", cbnRunId);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is int runId ? runId : null;
    }

    private static async Task<int> GetNextPeggingVersionAsync(
        SqlConnection connection, int cbnRunId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT ISNULL(MAX(version_no), 0) + 1 FROM pegging_runs WHERE cbn_run_id = @cbnRunId";
        command.Parameters.AddWithValue("@cbnRunId", cbnRunId);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static async Task<int> InsertPeggingRunAsync(
        SqlConnection connection, int cbnRunId, int versionNo, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO pegging_runs (cbn_run_id, version_no, status)
            OUTPUT INSERTED.id VALUES (@cbnRunId, @versionNo, 'COMPLETED')
            """;
        command.Parameters.AddWithValue("@cbnRunId", cbnRunId);
        command.Parameters.AddWithValue("@versionNo", versionNo);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static async Task<int> InsertLinkAsync(
        SqlConnection connection, int peggingRunId, PeggingEngine.PeggingLinkDraft draft, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO pegging_links (
                pegging_run_id, link_type, direction, source_entity_type, source_entity_id,
                target_entity_type, target_entity_id, material_requirement_id,
                quantity, unit, version_no, source_path)
            OUTPUT INSERTED.id
            VALUES (@runId, @linkType, @direction, @sourceType, @sourceId,
                    @targetType, @targetId, @reqId, @qty, @unit, 1, @path)
            """;
        command.Parameters.AddWithValue("@runId", peggingRunId);
        command.Parameters.AddWithValue("@linkType", draft.LinkType);
        command.Parameters.AddWithValue("@direction", draft.Direction);
        command.Parameters.AddWithValue("@sourceType", draft.SourceEntityType);
        command.Parameters.AddWithValue("@sourceId", draft.SourceEntityId);
        command.Parameters.AddWithValue("@targetType", draft.TargetEntityType);
        command.Parameters.AddWithValue("@targetId", draft.TargetEntityId);
        command.Parameters.AddWithValue("@reqId", (object?)draft.MaterialRequirementId ?? DBNull.Value);
        command.Parameters.AddWithValue("@qty", draft.Quantity);
        command.Parameters.AddWithValue("@unit", draft.Unit);
        command.Parameters.AddWithValue("@path", (object?)draft.SourcePath ?? DBNull.Value);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static async Task InsertLinkVersionAsync(
        SqlConnection connection, int linkId, int versionNo, double quantity, string reason, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO pegging_link_versions (pegging_link_id, version_no, quantity, change_reason)
            VALUES (@linkId, @versionNo, @qty, @reason)
            """;
        command.Parameters.AddWithValue("@linkId", linkId);
        command.Parameters.AddWithValue("@versionNo", versionNo);
        command.Parameters.AddWithValue("@qty", quantity);
        command.Parameters.AddWithValue("@reason", reason);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<List<PeggingLinkRow>> LoadLinksForRunAsync(
        SqlConnection connection,
        int peggingRunId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT pl.id, pl.link_type, pl.direction, pl.source_entity_type, pl.source_entity_id,
                   pl.target_entity_type, pl.target_entity_id, pl.quantity, pl.unit, pl.source_path, pl.version_no
            FROM pegging_links pl
            WHERE pl.pegging_run_id = @peggingRunId
            ORDER BY pl.id
            """;
        command.Parameters.AddWithValue("@peggingRunId", peggingRunId);

        var rawLinks = new List<(int Id, string LinkType, string Direction, string SourceType, int SourceId, string TargetType, int TargetId, double Qty, string Unit, string? Path, int Version)>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                rawLinks.Add((
                    reader.GetInt32(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetInt32(4),
                    reader.GetString(5),
                    reader.GetInt32(6),
                    reader.GetDouble(7),
                    reader.GetString(8),
                    reader.IsDBNull(9) ? null : reader.GetString(9),
                    reader.GetInt32(10)));
            }
        }

        var links = new List<PeggingLinkRow>();
        foreach (var row in rawLinks)
        {
            links.Add(new PeggingLinkRow(
                row.Id,
                row.LinkType,
                row.Direction,
                row.SourceType,
                row.SourceId,
                await ResolveLabelAsync(connection, row.SourceType, row.SourceId, cancellationToken),
                row.TargetType,
                row.TargetId,
                await ResolveLabelAsync(connection, row.TargetType, row.TargetId, cancellationToken),
                row.Qty,
                row.Unit,
                row.Path,
                row.Version));
        }

        return links;
    }

    private async Task<IReadOnlyList<PeggingCoverageRow>> BuildCoverageForRunAsync(
        SqlConnection connection,
        int peggingRunId,
        int cbnRunId,
        CancellationToken cancellationToken)
    {
        var links = await LoadLinksForRunAsync(connection, peggingRunId, cancellationToken);
        var byRequirement = links
            .Where(link => link.Direction == "DOWNSTREAM" && link.SourceEntityType == "MATERIAL_REQUIREMENT")
            .GroupBy(link => link.SourceEntityId)
            .ToDictionary(group => group.Key, group => group.ToList());

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT cmr.id, cmr.variant_label, a.code, a.label, a.article_type,
                   cmr.quantity_gross, cmr.unit, cmr.source_path
            FROM cbn_material_requirements cmr
            JOIN articles a ON a.id = cmr.component_article_id
            WHERE cmr.cbn_run_id = @cbnRunId
            ORDER BY cmr.variant_label, a.code
            """;
        command.Parameters.AddWithValue("@cbnRunId", cbnRunId);

        var coverage = new List<PeggingCoverageRow>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var requirementId = reader.GetInt32(0);
            byRequirement.TryGetValue(requirementId, out var requirementLinks);
            requirementLinks ??= [];

            var stock = requirementLinks.Where(link => link.TargetEntityType == "STOCK_BALANCE").Sum(link => link.Quantity);
            var manufacturing = requirementLinks.Where(link => link.TargetEntityType == "MANUFACTURING_ORDER").Sum(link => link.Quantity);
            var purchase = requirementLinks.Where(link => link.TargetEntityType == "PURCHASE_ORDER_LINE").Sum(link => link.Quantity);
            var required = reader.GetDouble(5);
            var remaining = Math.Max(0, required - stock - manufacturing - purchase);
            var status = remaining <= 0.0001
                ? "COMPLETE"
                : stock + manufacturing + purchase <= 0.0001
                    ? "UNPEGGED"
                    : "PARTIAL";

            coverage.Add(new PeggingCoverageRow(
                requirementId,
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                required,
                stock,
                manufacturing,
                purchase,
                remaining,
                reader.GetString(6),
                status,
                reader.GetString(7)));
        }

        return coverage;
    }

    private async Task<IReadOnlyList<PeggingSupplyUsageRow>> BuildSupplyUsageForRunAsync(
        SqlConnection connection,
        int peggingRunId,
        IReadOnlyList<PeggingEngine.SupplyCandidate> salesLines,
        IReadOnlyList<PeggingEngine.SupplyCandidate> mfgOrders,
        IReadOnlyList<PeggingEngine.SupplyCandidate> purchaseLines,
        IReadOnlyList<PeggingEngine.SupplyCandidate> stocks,
        CancellationToken cancellationToken)
    {
        var links = await LoadLinksForRunAsync(connection, peggingRunId, cancellationToken);
        var supplyLinks = links
            .Where(link => link.Direction == "UPSTREAM"
                && link.SourceEntityType is "STOCK_BALANCE" or "MANUFACTURING_ORDER" or "PURCHASE_ORDER_LINE" or "SALES_ORDER_LINE")
            .GroupBy(link => (link.SourceEntityType, link.SourceEntityId))
            .ToDictionary(group => group.Key, group => group.Sum(link => link.Quantity));

        var allSupplies = salesLines.Concat(mfgOrders).Concat(purchaseLines).Concat(stocks)
            .GroupBy(supply => (supply.EntityType, supply.EntityId))
            .Select(group => group.First())
            .ToList();

        var usage = new List<PeggingSupplyUsageRow>();
        foreach (var supply in allSupplies)
        {
            var peggedQuantity = supplyLinks.TryGetValue((supply.EntityType, supply.EntityId), out var qty) ? qty : 0;
            var remaining = Math.Max(0, supply.QuantityAvailable - peggedQuantity);
            var status = peggedQuantity <= 0.0001
                ? "UNUSED"
                : remaining <= 0.0001
                    ? "FULLY_ALLOCATED"
                    : "PARTIALLY_ALLOCATED";

            usage.Add(new PeggingSupplyUsageRow(
                supply.EntityType,
                supply.EntityId,
                await ResolveLabelAsync(connection, supply.EntityType, supply.EntityId, cancellationToken),
                await ResolveSupplyComponentCodeAsync(connection, supply, cancellationToken),
                supply.QuantityAvailable,
                peggedQuantity,
                remaining,
                supply.Unit,
                status));
        }

        return usage
            .OrderBy(row => row.SupplyEntityType)
            .ThenBy(row => row.SupplyLabel)
            .ToList();
    }

    private static async Task<IReadOnlyList<PeggingLinkHistoryRow>> LoadHistoryForRunAsync(
        SqlConnection connection,
        int peggingRunId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT pr.id, pr.version_no, plv.pegging_link_id, plv.version_no, plv.quantity, plv.change_reason, plv.changed_at
            FROM pegging_link_versions plv
            JOIN pegging_links pl ON pl.id = plv.pegging_link_id
            JOIN pegging_runs pr ON pr.id = pl.pegging_run_id
            WHERE pr.id = @peggingRunId
            ORDER BY plv.changed_at, plv.pegging_link_id, plv.version_no
            """;
        command.Parameters.AddWithValue("@peggingRunId", peggingRunId);

        var history = new List<PeggingLinkHistoryRow>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            history.Add(new PeggingLinkHistoryRow(
                reader.GetInt32(0),
                reader.GetInt32(1),
                reader.GetInt32(2),
                reader.GetInt32(3),
                reader.GetDouble(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.GetDateTime(6)));
        }

        return history;
    }

    private static async Task<IReadOnlyList<PeggingLinkHistoryRow>> LoadHistoryForCbnRunAsync(
        SqlConnection connection,
        int cbnRunId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT pr.id, pr.version_no, plv.pegging_link_id, plv.version_no, plv.quantity, plv.change_reason, plv.changed_at
            FROM pegging_link_versions plv
            JOIN pegging_links pl ON pl.id = plv.pegging_link_id
            JOIN pegging_runs pr ON pr.id = pl.pegging_run_id
            WHERE pr.cbn_run_id = @cbnRunId
            ORDER BY pr.version_no DESC, plv.changed_at DESC, plv.pegging_link_id DESC
            """;
        command.Parameters.AddWithValue("@cbnRunId", cbnRunId);

        var history = new List<PeggingLinkHistoryRow>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            history.Add(new PeggingLinkHistoryRow(
                reader.GetInt32(0),
                reader.GetInt32(1),
                reader.GetInt32(2),
                reader.GetInt32(3),
                reader.GetDouble(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.GetDateTime(6)));
        }

        return history;
    }

    private static async Task<string> ResolveSupplyComponentCodeAsync(
        SqlConnection connection,
        PeggingEngine.SupplyCandidate supply,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT code FROM articles WHERE id = @id";
        command.Parameters.AddWithValue("@id", supply.ArticleId);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result?.ToString() ?? supply.EntityCode;
    }

    private static async Task<string> ResolveLabelAsync(
        SqlConnection connection, string entityType, int entityId, CancellationToken cancellationToken)
    {
        var sql = entityType switch
        {
            "SALES_ORDER_LINE" => "SELECT so.code + ' L' + CAST(sol.line_no AS NVARCHAR(10)) FROM sales_order_lines sol JOIN sales_orders so ON so.id = sol.sales_order_id WHERE sol.id = @id",
            "MATERIAL_REQUIREMENT" => "SELECT a.code FROM cbn_material_requirements cmr JOIN articles a ON a.id = cmr.component_article_id WHERE cmr.id = @id",
            "MANUFACTURING_ORDER" => "SELECT code FROM manufacturing_orders WHERE id = @id",
            "PURCHASE_ORDER_LINE" => "SELECT po.code FROM purchase_order_lines pol JOIN purchase_orders po ON po.id = pol.purchase_order_id WHERE pol.id = @id",
            "STOCK_BALANCE" => "SELECT a.code FROM stock_balances sb JOIN articles a ON a.id = sb.article_id WHERE sb.id = @id",
            _ => "SELECT CAST(@id AS NVARCHAR(20))",
        };

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("@id", entityId);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result?.ToString() ?? entityId.ToString();
    }
}
