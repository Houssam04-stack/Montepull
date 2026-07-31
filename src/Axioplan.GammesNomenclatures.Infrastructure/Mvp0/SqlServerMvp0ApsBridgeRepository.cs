using Axioplan.GammesNomenclatures.Application.Abstractions;
using Axioplan.GammesNomenclatures.Application.Models;
using Axioplan.GammesNomenclatures.Domain.Mvp0;
using Axioplan.GammesNomenclatures.Infrastructure.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace Axioplan.GammesNomenclatures.Infrastructure.Mvp0;

public sealed class SqlServerMvp0ApsBridgeRepository(IOptions<DatabaseOptions> options) : IMvp0ApsBridgeRepository
{
    public async Task<Mvp0ApsBridgeResult> PromoteWorkingSetAsync(
        Mvp0CampaignDto campaign,
        IReadOnlyList<Mvp0ArticleRow> articles,
        IReadOnlyList<Mvp0BomRow> boms,
        IReadOnlyList<Mvp0RoutingOpRow> ops,
        IReadOnlySet<string> centers,
        IReadOnlySet<string> bomOpLinks,
        CancellationToken cancellationToken = default)
    {
        await using var connection = OpenConnection();
        await using var tx = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);

        var productFamilyId = await EnsureProductFamilyAsync(connection, tx, campaign.FamilyCode, cancellationToken);
        var articleFamilyId = await GetArticleFamilyIdAsync(connection, tx, campaign.FamilyCode, cancellationToken);

        var workcentersUpserted = 0;
        var wcMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var center in centers.Concat(ops.Select(o => o.CenterCode ?? "").Where(c => !string.IsNullOrWhiteSpace(c))).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var id = await UpsertWorkcenterAsync(connection, tx, center, cancellationToken);
            wcMap[center] = id;
            workcentersUpserted++;
        }

        var articleMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var articlesUpserted = 0;
        foreach (var row in articles.GroupBy(a => a.Code, StringComparer.OrdinalIgnoreCase).Select(g => g.First()))
        {
            var type = Mvp0ApsBridgeMapper.MapArticleType(row.Type);
            var unit = Mvp0ApsBridgeMapper.ResolveUnit(row.Unit);
            var id = await UpsertArticleAsync(connection, tx, articleFamilyId, row.Code, type, unit, row.Active, cancellationToken);
            articleMap[row.Code] = id;
            articlesUpserted++;
        }

        var (bomVersion, bomLines) = await CreateBomVersionAsync(
            connection, tx, productFamilyId, campaign.FamilyCode, boms, articleMap, ops, bomOpLinks, cancellationToken);

        var (routingVersion, routingOps) = await CreateRoutingVersionAsync(
            connection, tx, productFamilyId, campaign.FamilyCode, ops, articleMap, wcMap, cancellationToken);

        await tx.CommitAsync(cancellationToken);

        return new Mvp0ApsBridgeResult(
            campaign.FamilyCode,
            articlesUpserted,
            workcentersUpserted,
            bomLines,
            routingOps,
            bomOpLinks.Count,
            bomVersion,
            routingVersion);
    }

    private static async Task<int> EnsureProductFamilyAsync(
        SqlConnection connection,
        SqlTransaction tx,
        string familyCode,
        CancellationToken cancellationToken)
    {
        await using var find = connection.CreateCommand();
        find.Transaction = tx;
        find.CommandText = "SELECT id FROM product_families WHERE code = @code";
        find.Parameters.AddWithValue("@code", familyCode);
        var existing = await find.ExecuteScalarAsync(cancellationToken);
        if (existing is int id) return id;

        var articleFamilyId = await EnsureArticleFamilyAsync(connection, tx, familyCode, cancellationToken);
        await using var insert = connection.CreateCommand();
        insert.Transaction = tx;
        insert.CommandText = """
            INSERT INTO product_families (article_family_id, code, label)
            OUTPUT INSERTED.id
            VALUES (@af, @code, @label)
            """;
        insert.Parameters.AddWithValue("@af", articleFamilyId);
        insert.Parameters.AddWithValue("@code", familyCode);
        insert.Parameters.AddWithValue("@label", "Famille MVP-0 " + familyCode);
        return (int)(await insert.ExecuteScalarAsync(cancellationToken))!;
    }

    private static async Task<int> EnsureArticleFamilyAsync(
        SqlConnection connection,
        SqlTransaction tx,
        string familyCode,
        CancellationToken cancellationToken)
    {
        await using var find = connection.CreateCommand();
        find.Transaction = tx;
        find.CommandText = "SELECT id FROM article_families WHERE code = @code";
        find.Parameters.AddWithValue("@code", familyCode);
        var existing = await find.ExecuteScalarAsync(cancellationToken);
        if (existing is int id) return id;

        var categoryId = await ScalarIntAsync(connection, tx,
            "SELECT TOP 1 id FROM article_categories WHERE code = 'FINISHED_GOOD'", cancellationToken)
            ?? throw new InvalidOperationException("article_categories FINISHED_GOOD manquant.");

        await using var insert = connection.CreateCommand();
        insert.Transaction = tx;
        insert.CommandText = """
            INSERT INTO article_families (category_id, code, label)
            OUTPUT INSERTED.id
            VALUES (@cat, @code, @label)
            """;
        insert.Parameters.AddWithValue("@cat", categoryId);
        insert.Parameters.AddWithValue("@code", familyCode);
        insert.Parameters.AddWithValue("@label", "Famille " + familyCode);
        return (int)(await insert.ExecuteScalarAsync(cancellationToken))!;
    }

    private static async Task<int> GetArticleFamilyIdAsync(
        SqlConnection connection,
        SqlTransaction tx,
        string familyCode,
        CancellationToken cancellationToken)
    {
        await using var cmd = connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            SELECT af.id FROM article_families af
            JOIN product_families pf ON pf.article_family_id = af.id
            WHERE pf.code = @code
            """;
        cmd.Parameters.AddWithValue("@code", familyCode);
        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        if (result is int id) return id;
        return await EnsureArticleFamilyAsync(connection, tx, familyCode, cancellationToken);
    }

    private static async Task<int> UpsertWorkcenterAsync(
        SqlConnection connection,
        SqlTransaction tx,
        string code,
        CancellationToken cancellationToken)
    {
        await using var merge = connection.CreateCommand();
        merge.Transaction = tx;
        merge.CommandText = """
            MERGE workcenters AS t
            USING (SELECT @code AS code, @label AS label) AS s
            ON t.code = s.code
            WHEN MATCHED THEN UPDATE SET label = s.label
            WHEN NOT MATCHED THEN INSERT (code, label, source_system) VALUES (s.code, s.label, 'MVP0_BRIDGE')
            OUTPUT INSERTED.id;
            """;
        merge.Parameters.AddWithValue("@code", code);
        merge.Parameters.AddWithValue("@label", "Centre " + code);
        return (int)(await merge.ExecuteScalarAsync(cancellationToken))!;
    }

    private static async Task<int> UpsertArticleAsync(
        SqlConnection connection,
        SqlTransaction tx,
        int articleFamilyId,
        string code,
        string articleType,
        string unit,
        bool active,
        CancellationToken cancellationToken)
    {
        await using var merge = connection.CreateCommand();
        merge.Transaction = tx;
        merge.CommandText = """
            MERGE articles AS t
            USING (SELECT @code AS code) AS s ON t.code = s.code
            WHEN MATCHED THEN UPDATE SET
                label = @label,
                article_type = @type,
                default_unit = @unit,
                status = @status,
                source_system = 'MVP0_BRIDGE',
                creation_mode = 'IMPORTED'
            WHEN NOT MATCHED THEN INSERT
                (family_id, code, label, article_type, default_unit, status, source_system, creation_mode)
                VALUES (@fam, @code, @label, @type, @unit, @status, 'MVP0_BRIDGE', 'IMPORTED')
            OUTPUT INSERTED.id;
            """;
        merge.Parameters.AddWithValue("@fam", articleFamilyId);
        merge.Parameters.AddWithValue("@code", code);
        merge.Parameters.AddWithValue("@label", code);
        merge.Parameters.AddWithValue("@type", articleType);
        merge.Parameters.AddWithValue("@unit", unit);
        merge.Parameters.AddWithValue("@status", active ? "ACTIVE" : "INACTIVE");
        return (int)(await merge.ExecuteScalarAsync(cancellationToken))!;
    }

    private static async Task<(int Version, int Lines)> CreateBomVersionAsync(
        SqlConnection connection,
        SqlTransaction tx,
        int productFamilyId,
        string familyCode,
        IReadOnlyList<Mvp0BomRow> boms,
        IReadOnlyDictionary<string, int> articleMap,
        IReadOnlyList<Mvp0RoutingOpRow> ops,
        IReadOnlySet<string> bomOpLinks,
        CancellationToken cancellationToken)
    {
        var version = await ScalarIntAsync(connection, tx,
            "SELECT ISNULL(MAX(version), 0) + 1 FROM bom_bases WHERE product_family_id = @pf",
            cancellationToken,
            new SqlParameter("@pf", productFamilyId)) ?? 1;

        var bomCode = $"{familyCode}-MVP0-BOM-v{version}";
        await using var insertBom = connection.CreateCommand();
        insertBom.Transaction = tx;
        insertBom.CommandText = """
            INSERT INTO bom_bases (product_family_id, code, version, status, aps_valid_from)
            OUTPUT INSERTED.id
            VALUES (@pf, @code, @ver, 'VALIDATED', CAST(GETDATE() AS DATE))
            """;
        insertBom.Parameters.AddWithValue("@pf", productFamilyId);
        insertBom.Parameters.AddWithValue("@code", bomCode);
        insertBom.Parameters.AddWithValue("@ver", version);
        var bomId = (int)(await insertBom.ExecuteScalarAsync(cancellationToken))!;

        var lineNo = 0;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in boms)
        {
            var key = row.Parent + "|" + row.Component;
            if (!seen.Add(key)) continue;
            if (!articleMap.TryGetValue(row.Parent, out _) || !articleMap.TryGetValue(row.Component, out var compId))
                continue;

            lineNo++;
            var opCode = Mvp0ApsBridgeMapper.ResolveOperationCode(row.Parent, ops);
            await using var line = connection.CreateCommand();
            line.Transaction = tx;
            line.CommandText = """
                INSERT INTO bom_base_lines
                    (bom_base_id, line_no, component_article_id, quantity_base, unit, loss_rate, behavior, aps_operation_code)
                VALUES (@bom, @ln, @comp, @qty, @unit, @loss, 'FIXED', @op)
                """;
            line.Parameters.AddWithValue("@bom", bomId);
            line.Parameters.AddWithValue("@ln", lineNo);
            line.Parameters.AddWithValue("@comp", compId);
            line.Parameters.AddWithValue("@qty", row.Qty);
            line.Parameters.AddWithValue("@unit", Mvp0ApsBridgeMapper.ResolveUnit(row.Unit));
            line.Parameters.AddWithValue("@loss", row.ScrapRate);
            line.Parameters.AddWithValue("@op", (object?)opCode ?? DBNull.Value);
            await line.ExecuteNonQueryAsync(cancellationToken);
        }

        await AssignLatestBomAsync(connection, tx, productFamilyId, bomId, cancellationToken);
        return (version, lineNo);
    }

    private static async Task AssignLatestBomAsync(
        SqlConnection connection,
        SqlTransaction tx,
        int productFamilyId,
        int bomId,
        CancellationToken cancellationToken)
    {
        await using var cmd = connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            MERGE article_bom_assignments AS target
            USING (
                SELECT a.id AS article_id, @bomId AS bom_base_id
                FROM articles a
                JOIN article_families af ON af.id = a.family_id
                JOIN product_families pf ON pf.article_family_id = af.id
                WHERE pf.id = @pfId AND a.article_type = 'FINISHED_GOOD'
            ) AS source ON target.article_id = source.article_id
            WHEN MATCHED THEN UPDATE SET bom_base_id = source.bom_base_id
            WHEN NOT MATCHED THEN INSERT (article_id, bom_base_id) VALUES (source.article_id, source.bom_base_id);
            """;
        cmd.Parameters.AddWithValue("@bomId", bomId);
        cmd.Parameters.AddWithValue("@pfId", productFamilyId);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<(int Version, int Ops)> CreateRoutingVersionAsync(
        SqlConnection connection,
        SqlTransaction tx,
        int productFamilyId,
        string familyCode,
        IReadOnlyList<Mvp0RoutingOpRow> ops,
        IReadOnlyDictionary<string, int> articleMap,
        IReadOnlyDictionary<string, int> wcMap,
        CancellationToken cancellationToken)
    {
        var version = await ScalarIntAsync(connection, tx,
            "SELECT ISNULL(MAX(version), 0) + 1 FROM routing_bases WHERE product_family_id = @pf",
            cancellationToken,
            new SqlParameter("@pf", productFamilyId)) ?? 1;

        var routingCode = $"{familyCode}-MVP0-RTG-v{version}";
        await using var insertRtg = connection.CreateCommand();
        insertRtg.Transaction = tx;
        insertRtg.CommandText = """
            INSERT INTO routing_bases (product_family_id, code, version, status)
            OUTPUT INSERTED.id
            VALUES (@pf, @code, @ver, 'VALIDATED')
            """;
        insertRtg.Parameters.AddWithValue("@pf", productFamilyId);
        insertRtg.Parameters.AddWithValue("@code", routingCode);
        insertRtg.Parameters.AddWithValue("@ver", version);
        var routingId = (int)(await insertRtg.ExecuteScalarAsync(cancellationToken))!;

        var opNo = 0;
        foreach (var row in ops.OrderBy(o => o.Article).ThenBy(o => o.Sequence))
        {
            if (!articleMap.ContainsKey(row.Article)) continue;
            var center = row.CenterCode ?? "UNKNOWN";
            if (!wcMap.TryGetValue(center, out var wcId)) continue;

            opNo++;
            await using var op = connection.CreateCommand();
            op.Transaction = tx;
            op.CommandText = """
                INSERT INTO routing_base_operations
                    (routing_base_id, operation_no, name, workcenter_id, quantity_base, time_base, time_unit, behavior)
                VALUES (@rtg, @no, @name, @wc, 1, @time, 'HOUR', 'VALIDATED')
                """;
            op.Parameters.AddWithValue("@rtg", routingId);
            op.Parameters.AddWithValue("@no", opNo);
            op.Parameters.AddWithValue("@name", row.OpCode);
            op.Parameters.AddWithValue("@wc", wcId);
            op.Parameters.AddWithValue("@time", row.CycleTime + row.SetupTime);
            await op.ExecuteNonQueryAsync(cancellationToken);
        }

        return (version, opNo);
    }

    private static async Task<int?> ScalarIntAsync(
        SqlConnection connection,
        SqlTransaction tx,
        string sql,
        CancellationToken cancellationToken,
        params SqlParameter[] parameters)
    {
        await using var cmd = connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        cmd.Parameters.AddRange(parameters);
        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return result is null or DBNull ? null : Convert.ToInt32(result);
    }

    private SqlConnection OpenConnection()
    {
        var cs = options.Value.ConnectionString ?? throw new InvalidOperationException("Database:ConnectionString manquant.");
        var c = new SqlConnection(cs);
        c.Open();
        return c;
    }
}
