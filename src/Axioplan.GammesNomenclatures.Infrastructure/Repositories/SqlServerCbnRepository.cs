using Axioplan.GammesNomenclatures.Application.Abstractions;
using Axioplan.GammesNomenclatures.Application.Models;
using Axioplan.GammesNomenclatures.Domain;
using Axioplan.GammesNomenclatures.Infrastructure.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace Axioplan.GammesNomenclatures.Infrastructure.Repositories;

public sealed class SqlServerCbnRepository(IOptions<DatabaseOptions> options) : ICbnRepository
{
    private sealed record OrderLineContext(
        int LineId,
        int LineNo,
        string ArticleCode,
        string? SizeCode,
        string? ColorCode,
        double Quantity,
        string Unit,
        string? ExternalRef);

    private sealed record BomLineContext(
        int LineNo,
        int ParentArticleId,
        int ComponentArticleId,
        string ComponentCode,
        string ComponentLabel,
        string ComponentType,
        double QuantityBase,
        string Unit,
        double LossRate,
        string Behavior,
        int BomLevel,
        string SourcePath);

    public async Task<IReadOnlyList<SalesOrderListItem>> GetSalesOrdersAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT so.id, so.code, so.label, so.status, COUNT(sol.id) AS line_count
            FROM sales_orders so
            LEFT JOIN sales_order_lines sol ON sol.sales_order_id = so.id
            GROUP BY so.id, so.code, so.label, so.status
            ORDER BY so.code
            """;

        var items = new List<SalesOrderListItem>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new SalesOrderListItem(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.GetString(3),
                reader.GetInt32(4)));
        }

        return items;
    }

    public async Task<IReadOnlyList<SalesOrderLineItem>> GetSalesOrderLinesAsync(
        int salesOrderId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT sol.id, sol.line_no, a.code,
                   sz.technical_code, col.technical_code,
                   sol.quantity, sol.unit, sol.external_ref
            FROM sales_order_lines sol
            JOIN articles a ON a.id = sol.article_id
            LEFT JOIN attribute_options sz ON sz.id = sol.size_option_id
            LEFT JOIN attribute_options col ON col.id = sol.color_option_id
            WHERE sol.sales_order_id = @salesOrderId
            ORDER BY sol.line_no
            """;
        command.Parameters.AddWithValue("@salesOrderId", salesOrderId);

        var items = new List<SalesOrderLineItem>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new SalesOrderLineItem(
                reader.GetInt32(0),
                reader.GetInt32(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.GetDouble(5),
                reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetString(7)));
        }

        return items;
    }

    public async Task<CbnRunResult> RunCbnAsync(CbnRunRequest request, CancellationToken cancellationToken = default)
    {
        await using var connection = OpenConnection();

        var orderCode = await LoadOrderCodeAsync(connection, request.SalesOrderId, cancellationToken)
            ?? throw new InvalidOperationException($"Commande introuvable : {request.SalesOrderId}");

        var familyId = await LoadFamilyIdAsync(connection, request.ProductFamilyCode, cancellationToken)
            ?? throw new InvalidOperationException($"Famille introuvable : {request.ProductFamilyCode}");

        var cbnParams = await LoadCbnParametersAsync(connection, cancellationToken);
        await ValidateCbnPreconditionsAsync(connection, request.ProductFamilyCode, cbnParams, cancellationToken);

        var orderLines = await LoadOrderLinesForFamilyAsync(
            connection, request.SalesOrderId, familyId, cancellationToken);

        if (orderLines.Count == 0)
        {
            throw new InvalidOperationException(
                "Aucune ligne de commande pour cette famille. Verifiez la commande et la famille produit.");
        }

        var maxCascade = int.Parse(cbnParams.GetValueOrDefault("PEGGING_MAX_CASCADE_LEVEL", "5"));
        var parentArticleId = await LoadFinishedGoodArticleIdAsync(connection, request.ProductFamilyCode, cancellationToken);
        var bomLines = await LoadFlattenedBomLinesAsync(
            connection, request.ProductFamilyCode, parentArticleId, maxCascade, cancellationToken);

        if (bomLines.Count == 0)
        {
            throw new InvalidOperationException($"Nomenclature de base introuvable pour {request.ProductFamilyCode}.");
        }

        var runId = await InsertCbnRunAsync(connection, request.SalesOrderId, familyId, cancellationToken);
        var requirementFormula = await LoadRequirementFormulaAsync(connection, request.ProductFamilyCode, cancellationToken);

        var flattenedRows = new List<CbnFlattenedBomRow>();
        var requirements = new List<CbnRequirementRow>();
        var traces = new List<CbnTraceRow>();
        var requirementDrafts = new List<CbnEngine.MaterialRequirementDraft>();

        foreach (var bomLine in bomLines)
        {
            await InsertFlattenedBomLineAsync(connection, runId, bomLine, cancellationToken);
            flattenedRows.Add(new CbnFlattenedBomRow(
                bomLine.LineNo,
                bomLine.ComponentCode,
                bomLine.ComponentLabel,
                bomLine.ComponentType,
                ComputeCalculationMode(bomLine.Behavior),
                bomLine.BomLevel,
                bomLine.QuantityBase,
                bomLine.Unit,
                bomLine.LossRate,
                bomLine.Behavior,
                bomLine.SourcePath));
        }

        foreach (var line in orderLines)
        {
            var variantLabel = BuildVariantLabel(line);
            var sizeCoeff = line.SizeCode is not null
                ? await LoadCoefficientAsync(connection, request.ProductFamilyCode, "SIZE", line.SizeCode, cancellationToken)
                : 1.0;
            var colorCoeff = line.ColorCode is not null
                ? await LoadCoefficientAsync(connection, request.ProductFamilyCode, "COLOR", line.ColorCode, cancellationToken)
                : 1.0;
            var argumentCoeffs = await BuildArgumentCoefficientMapAsync(
                connection,
                request.ProductFamilyCode,
                line.SizeCode,
                line.ColorCode,
                cancellationToken);

            foreach (var bomLine in bomLines)
            {
                var engineLine = new CbnEngine.FlattenedBomLine(
                    bomLine.LineNo,
                    bomLine.ComponentCode,
                    bomLine.QuantityBase,
                    bomLine.Unit,
                    bomLine.LossRate,
                    bomLine.Behavior);

                var draft = CbnEngine.ComputeRequirement(
                    engineLine,
                    line.Quantity,
                    variantLabel,
                    sizeCoeff,
                    colorCoeff,
                    requirementFormula?.Expression,
                    requirementFormula?.ApplyOrderQuantity ?? true,
                    argumentCoeffs);

                requirementDrafts.Add(draft);

                await InsertRequirementAsync(connection, runId, line.LineId, bomLine, draft, cancellationToken);

                requirements.Add(new CbnRequirementRow(
                    variantLabel,
                    bomLine.ComponentCode,
                    bomLine.ComponentLabel,
                    bomLine.ComponentType,
                    draft.CalculationMode,
                    draft.QuantityNet,
                    draft.QuantityGross,
                    draft.Unit,
                    draft.SourcePath));

                await InsertTraceAsync(connection, runId, line.LineId, variantLabel, draft.Trace, cancellationToken);
                traces.Add(new CbnTraceRow(
                    variantLabel,
                    draft.Trace.ComponentCode,
                    draft.Trace.CalculationMode,
                    draft.Trace.RuleCode,
                    draft.Trace.BaseValue,
                    draft.Trace.OrderQuantity,
                    draft.Trace.SizeCoefficient,
                    draft.Trace.ColorCoefficient,
                    draft.Trace.LossRate,
                    draft.Trace.QuantityNet,
                    draft.Trace.QuantityGross,
                    draft.Trace.Unit,
                    draft.Trace.Formula));
            }
        }

        var componentTotals = CbnEngine.AggregateTotals(requirementDrafts)
            .Select(total =>
            {
                var label = bomLines.FirstOrDefault(b => b.ComponentCode == total.ComponentCode)?.ComponentLabel
                    ?? total.ComponentCode;
                var componentType = bomLines.FirstOrDefault(b => b.ComponentCode == total.ComponentCode)?.ComponentType
                    ?? "COMPONENT";
                return new CbnComponentTotal(total.ComponentCode, label, componentType, total.CalculationMode, total.TotalNet, total.TotalGross, total.Unit);
            })
            .ToList();

        return new CbnRunResult
        {
            RunId = runId,
            SalesOrderCode = orderCode,
            ProductFamilyCode = request.ProductFamilyCode,
            Status = "COMPLETED",
            FlattenedBom = flattenedRows,
            Requirements = requirements,
            ComponentTotals = componentTotals,
            Traces = traces,
        };
    }

    private SqlConnection OpenConnection()
    {
        var connectionString = options.Value.ConnectionString;
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Chaine de connexion SQL Server introuvable. Configurez Database:ConnectionString ou lancez : python scripts/init_db.py");
        }

        var connection = new SqlConnection(connectionString);
        connection.Open();
        return connection;
    }

    private static string BuildVariantLabel(OrderLineContext line)
    {
        var parts = new List<string> { line.ArticleCode };
        if (!string.IsNullOrWhiteSpace(line.SizeCode))
        {
            parts.Add(line.SizeCode);
        }

        if (!string.IsNullOrWhiteSpace(line.ColorCode))
        {
            parts.Add(line.ColorCode);
        }

        return string.Join("-", parts);
    }

    private static string ComputeCalculationMode(string behavior) => behavior switch
    {
        "CALCULATED" => "VARIABLE_MATERIAL",
        "COPY_TO_VALIDATE" => "TO_CONFIRM",
        _ => "FIXED_SUPPLY",
    };

    private static async Task<string?> LoadOrderCodeAsync(
        SqlConnection connection,
        int salesOrderId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT code FROM sales_orders WHERE id = @id";
        command.Parameters.AddWithValue("@id", salesOrderId);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result as string;
    }

    private static async Task<int?> LoadFamilyIdAsync(
        SqlConnection connection,
        string familyCode,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT id FROM product_families WHERE code = @code";
        command.Parameters.AddWithValue("@code", familyCode);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is int id ? id : null;
    }

    private static async Task<Dictionary<string, string>> LoadCbnParametersAsync(
        SqlConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT p.code, pv.value_text
            FROM mvp_parameters p
            JOIN mvp_parameter_groups g ON g.id = p.group_id
            LEFT JOIN mvp_parameter_values pv ON pv.parameter_id = p.id AND pv.scope_key IS NULL
            WHERE g.code IN ('CBN', 'PEGGING')
            """;

        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            parameters[reader.GetString(0)] = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
        }

        return parameters;
    }

    private static async Task ValidateCbnPreconditionsAsync(
        SqlConnection connection,
        string familyCode,
        Dictionary<string, string> cbnParams,
        CancellationToken cancellationToken)
    {
        var requireValidated = cbnParams.GetValueOrDefault("CBN_REQUIRE_VALIDATED_PROFILE", "true")
            .Equals("true", StringComparison.OrdinalIgnoreCase);

        if (requireValidated)
        {
            await using var profileCommand = connection.CreateCommand();
            profileCommand.CommandText = """
                SELECT TOP 1 gp.status
                FROM generation_profiles gp
                JOIN product_families pf ON pf.id = gp.product_family_id
                WHERE pf.code = @familyCode AND gp.can_generate_bom = 1
                ORDER BY gp.id
                """;
            profileCommand.Parameters.AddWithValue("@familyCode", familyCode);

            var profileStatus = await profileCommand.ExecuteScalarAsync(cancellationToken) as string;
            if (profileStatus is null || !GenerationEngine.ProfileCanGenerate(profileStatus))
            {
                throw new InvalidOperationException(
                    "Profil de generation non valide pour CBN. Parametre CBN_REQUIRE_VALIDATED_PROFILE actif.");
            }
        }

        var acceptedStatuses = cbnParams.GetValueOrDefault("CBN_ACCEPTED_BOM_STATUSES", "VALIDATED,GENERATED")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => s.ToUpperInvariant())
            .ToHashSet();

        await using var bomCommand = connection.CreateCommand();
        bomCommand.CommandText = """
            SELECT TOP 1 bb.status
            FROM bom_bases bb
            JOIN product_families pf ON pf.id = bb.product_family_id
            WHERE pf.code = @familyCode
            ORDER BY bb.version DESC
            """;
        bomCommand.Parameters.AddWithValue("@familyCode", familyCode);

        var bomStatus = (await bomCommand.ExecuteScalarAsync(cancellationToken) as string)?.ToUpperInvariant();
        if (bomStatus is null)
        {
            throw new InvalidOperationException($"Nomenclature de base introuvable pour {familyCode}.");
        }

        if (!acceptedStatuses.Contains(bomStatus))
        {
            throw new InvalidOperationException(
                $"Statut BOM '{bomStatus}' non accepte pour CBN. Acceptes : {string.Join(", ", acceptedStatuses)}. A confirmer.");
        }
    }

    private static async Task<List<OrderLineContext>> LoadOrderLinesForFamilyAsync(
        SqlConnection connection,
        int salesOrderId,
        int familyId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT sol.id, sol.line_no, a.code,
                   sz.technical_code, col.technical_code,
                   sol.quantity, sol.unit, sol.external_ref
            FROM sales_order_lines sol
            JOIN articles a ON a.id = sol.article_id
            LEFT JOIN attribute_options sz ON sz.id = sol.size_option_id
            LEFT JOIN attribute_options col ON col.id = sol.color_option_id
            WHERE sol.sales_order_id = @salesOrderId
              AND (sol.product_family_id = @familyId OR sol.product_family_id IS NULL)
            ORDER BY sol.line_no
            """;
        command.Parameters.AddWithValue("@salesOrderId", salesOrderId);
        command.Parameters.AddWithValue("@familyId", familyId);

        var lines = new List<OrderLineContext>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            lines.Add(new OrderLineContext(
                reader.GetInt32(0),
                reader.GetInt32(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.GetDouble(5),
                reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetString(7)));
        }

        return lines;
    }

    private static async Task<int> LoadFinishedGoodArticleIdAsync(
        SqlConnection connection,
        string familyCode,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT TOP 1 a.id
            FROM articles a
            JOIN article_families af ON af.id = a.family_id
            JOIN product_families pf ON pf.article_family_id = af.id
            WHERE pf.code = @familyCode AND a.article_type = 'FINISHED_GOOD'
            """;
        command.Parameters.AddWithValue("@familyCode", familyCode);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is int id ? id : throw new InvalidOperationException($"Article fini introuvable pour {familyCode}.");
    }

    private static async Task<List<BomLineContext>> LoadFlattenedBomLinesAsync(
        SqlConnection connection,
        string familyCode,
        int parentArticleId,
        int maxCascadeLevel,
        CancellationToken cancellationToken)
    {
        var rootLines = await LoadBomLinesForFamilyAsync(connection, familyCode, cancellationToken);
        var subBomCache = await LoadAllArticleSubBomsAsync(connection, cancellationToken);
        var flattened = BomFlattener.Flatten(
            rootLines,
            articleId => subBomCache.TryGetValue(articleId, out var lines) ? lines : null,
            maxCascadeLevel);

        return flattened.Select(line => new BomLineContext(
            line.LineNo,
            parentArticleId,
            line.ComponentArticleId,
            line.ComponentCode,
            line.ComponentLabel,
            line.ComponentType,
            line.QuantityPerUnit,
            line.Unit,
            line.LossRate,
            line.Behavior,
            line.BomLevel,
            line.SourcePath)).ToList();
    }

    private static async Task<Dictionary<int, IReadOnlyList<BomFlattener.BomLineInput>>> LoadAllArticleSubBomsAsync(
        SqlConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT aba.article_id, bl.line_no, a.id, a.code, a.label, a.article_type,
                   bl.quantity_base, bl.unit, bl.loss_rate, bl.behavior
            FROM article_bom_assignments aba
            JOIN bom_base_lines bl ON bl.bom_base_id = aba.bom_base_id
            JOIN articles a ON a.id = bl.component_article_id
            ORDER BY aba.article_id, bl.line_no
            """;

        var cache = new Dictionary<int, List<BomFlattener.BomLineInput>>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var articleId = reader.GetInt32(0);
            if (!cache.TryGetValue(articleId, out var lines))
            {
                lines = [];
                cache[articleId] = lines;
            }

            lines.Add(new BomFlattener.BomLineInput(
                reader.GetInt32(1),
                reader.GetInt32(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetDouble(6),
                reader.GetString(7),
                reader.GetDouble(8),
                reader.GetString(9)));
        }

        return cache.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<BomFlattener.BomLineInput>)pair.Value);
    }

    private static async Task<IReadOnlyList<BomFlattener.BomLineInput>> LoadBomLinesForFamilyAsync(
        SqlConnection connection,
        string familyCode,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT bl.line_no, a.id, a.code, a.label, a.article_type,
                   bl.quantity_base, bl.unit, bl.loss_rate, bl.behavior
            FROM bom_base_lines bl
            JOIN bom_bases b ON b.id = bl.bom_base_id
            JOIN product_families pf ON pf.id = b.product_family_id
            JOIN articles a ON a.id = bl.component_article_id
            WHERE pf.code = @familyCode
              AND b.id = (
                  SELECT TOP 1 b2.id
                  FROM bom_bases b2
                  WHERE b2.product_family_id = pf.id
                  ORDER BY b2.version DESC, b2.id DESC
              )
            ORDER BY bl.line_no
            """;
        command.Parameters.AddWithValue("@familyCode", familyCode);
        return await ReadBomLineInputsAsync(command, cancellationToken);
    }

    private static async Task<List<BomFlattener.BomLineInput>> ReadBomLineInputsAsync(
        SqlCommand command,
        CancellationToken cancellationToken)
    {
        var lines = new List<BomFlattener.BomLineInput>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            lines.Add(new BomFlattener.BomLineInput(
                reader.GetInt32(0),
                reader.GetInt32(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetDouble(5),
                reader.GetString(6),
                reader.GetDouble(7),
                reader.GetString(8)));
        }

        return lines;
    }

    private static async Task<int> InsertCbnRunAsync(
        SqlConnection connection,
        int salesOrderId,
        int familyId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO cbn_runs (sales_order_id, product_family_id, status)
            OUTPUT INSERTED.id
            VALUES (@salesOrderId, @familyId, 'COMPLETED')
            """;
        command.Parameters.AddWithValue("@salesOrderId", salesOrderId);
        command.Parameters.AddWithValue("@familyId", familyId);

        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static async Task InsertFlattenedBomLineAsync(
        SqlConnection connection,
        int runId,
        BomLineContext bomLine,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO cbn_flattened_bom_lines (
                cbn_run_id, parent_article_id, component_article_id, bom_line_no, bom_level,
                quantity_per_unit, unit, loss_rate, behavior, source_path)
            VALUES (@runId, @parentId, @componentId, @lineNo, @level, @qty, @unit, @loss, @behavior, @path)
            """;
        command.Parameters.AddWithValue("@runId", runId);
        command.Parameters.AddWithValue("@parentId", bomLine.ParentArticleId);
        command.Parameters.AddWithValue("@componentId", bomLine.ComponentArticleId);
        command.Parameters.AddWithValue("@lineNo", bomLine.LineNo);
        command.Parameters.AddWithValue("@level", bomLine.BomLevel);
        command.Parameters.AddWithValue("@qty", bomLine.QuantityBase);
        command.Parameters.AddWithValue("@unit", bomLine.Unit);
        command.Parameters.AddWithValue("@loss", bomLine.LossRate);
        command.Parameters.AddWithValue("@behavior", bomLine.Behavior);
        command.Parameters.AddWithValue("@path", bomLine.SourcePath);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<int> InsertRequirementAsync(
        SqlConnection connection,
        int runId,
        int lineId,
        BomLineContext bomLine,
        CbnEngine.MaterialRequirementDraft draft,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO cbn_material_requirements (
                cbn_run_id, sales_order_line_id, component_article_id, variant_label,
                quantity_net, quantity_gross, unit, source_path)
            OUTPUT INSERTED.id
            VALUES (@runId, @lineId, @componentId, @variant, @net, @gross, @unit, @path)
            """;
        command.Parameters.AddWithValue("@runId", runId);
        command.Parameters.AddWithValue("@lineId", lineId);
        command.Parameters.AddWithValue("@componentId", bomLine.ComponentArticleId);
        command.Parameters.AddWithValue("@variant", draft.VariantLabel);
        command.Parameters.AddWithValue("@net", draft.QuantityNet);
        command.Parameters.AddWithValue("@gross", draft.QuantityGross);
        command.Parameters.AddWithValue("@unit", draft.Unit);
        command.Parameters.AddWithValue("@path", draft.SourcePath);

        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static async Task InsertTraceAsync(
        SqlConnection connection,
        int runId,
        int lineId,
        string variantLabel,
        CbnEngine.CalculationTraceDraft trace,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO cbn_calculation_traces (
                cbn_run_id, sales_order_line_id, object_type, component_code, rule_code,
                base_value, order_quantity, size_coefficient, color_coefficient, loss_rate,
                quantity_net, quantity_gross, unit, formula)
            VALUES (
                @runId, @lineId, @objectType, @component, @rule,
                @base, @orderQty, @sizeCoeff, @colorCoeff, @loss,
                @net, @gross, @unit, @formula)
            """;
        command.Parameters.AddWithValue("@runId", runId);
        command.Parameters.AddWithValue("@lineId", lineId);
        command.Parameters.AddWithValue("@objectType", trace.ObjectType);
        command.Parameters.AddWithValue("@component", trace.ComponentCode);
        command.Parameters.AddWithValue("@rule", trace.RuleCode);
        command.Parameters.AddWithValue("@base", trace.BaseValue);
        command.Parameters.AddWithValue("@orderQty", trace.OrderQuantity);
        command.Parameters.AddWithValue("@sizeCoeff", (object?)trace.SizeCoefficient ?? DBNull.Value);
        command.Parameters.AddWithValue("@colorCoeff", (object?)trace.ColorCoefficient ?? DBNull.Value);
        command.Parameters.AddWithValue("@loss", trace.LossRate);
        command.Parameters.AddWithValue("@net", trace.QuantityNet);
        command.Parameters.AddWithValue("@gross", trace.QuantityGross);
        command.Parameters.AddWithValue("@unit", trace.Unit);
        command.Parameters.AddWithValue("@formula", trace.Formula);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<double> LoadCoefficientAsync(
        SqlConnection connection,
        string familyCode,
        string attributeCode,
        string optionCode,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT cc.coefficient
            FROM consumption_coefficients cc
            JOIN product_families pf ON pf.id = cc.product_family_id
            JOIN attribute_definitions ad ON ad.id = cc.attribute_id
            JOIN attribute_options ao ON ao.id = cc.option_id
            WHERE pf.code = @familyCode
              AND ad.code = @attributeCode
              AND ao.technical_code = @optionCode
              AND cc.status = 'VALIDATED'
            """;
        command.Parameters.AddWithValue("@familyCode", familyCode);
        command.Parameters.AddWithValue("@attributeCode", attributeCode);
        command.Parameters.AddWithValue("@optionCode", optionCode);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is double coefficient ? coefficient : Convert.ToDouble(result ?? 1.0);
    }

    private sealed record RequirementFormulaRow(string Expression, bool ApplyOrderQuantity);

    private static async Task<RequirementFormulaRow?> LoadRequirementFormulaAsync(
        SqlConnection connection,
        string familyCode,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT rf.expression, rf.apply_order_quantity
            FROM requirement_formulas rf
            JOIN product_families pf ON pf.id = rf.product_family_id
            WHERE pf.code = @familyCode
              AND rf.target = 'REQUIREMENT'
              AND rf.status = 'VALIDATED'
            """;
        command.Parameters.AddWithValue("@familyCode", familyCode);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new RequirementFormulaRow(reader.GetString(0), reader.GetBoolean(1));
    }

    private static async Task<Dictionary<string, double>> BuildArgumentCoefficientMapAsync(
        SqlConnection connection,
        string familyCode,
        string? sizeCode,
        string? colorCode,
        CancellationToken cancellationToken)
    {
        var coefficients = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(sizeCode))
        {
            coefficients["SIZE"] = await LoadCoefficientAsync(connection, familyCode, "SIZE", sizeCode, cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(colorCode))
        {
            coefficients["COLOR"] = await LoadCoefficientAsync(connection, familyCode, "COLOR", colorCode, cancellationToken);
        }

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT ad.code
            FROM attribute_definitions ad
            WHERE ad.is_formula_argument = 1
              AND ad.is_active = 1
              AND ad.code NOT IN ('SIZE', 'COLOR')
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            coefficients.TryAdd(reader.GetString(0), 1.0);
        }

        return coefficients;
    }
}
