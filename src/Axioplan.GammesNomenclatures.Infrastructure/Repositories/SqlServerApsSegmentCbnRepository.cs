using System.Text.Json;
using Axioplan.GammesNomenclatures.Application.Abstractions;
using Axioplan.GammesNomenclatures.Application.Aps;
using Axioplan.GammesNomenclatures.Application.Models;
using Axioplan.GammesNomenclatures.Domain.Aps.Compiler;
using Axioplan.GammesNomenclatures.Domain.Aps.SegmentCbn;
using Axioplan.GammesNomenclatures.Infrastructure.Aps;
using Microsoft.Data.SqlClient;

namespace Axioplan.GammesNomenclatures.Infrastructure.Repositories;

public sealed class SqlServerApsSegmentCbnRepository(
    ApsSchemaBootstrap schema,
    IApsCompilerRepository compilerRepository) : IApsSegmentCbnRepository
{
    public Task EnsureSchemaAsync(CancellationToken cancellationToken = default)
        => schema.EnsureAsync(cancellationToken);

    public async Task SeedDemoAsync(CancellationToken cancellationToken = default)
    {
        await schema.EnsureAsync(cancellationToken);
        await compilerRepository.EnsureSchemaAsync(cancellationToken);

        await EnsureCompiledAsync("DEMO_PF", ApsSegments.A, "CIR_TRICOT_INT",
            [new CompileBomLineInput("FIL-DEMO", 0.2, "TRICOTAGE")],
            [new CompileRoutingOpInput("TRICOTAGE", "CC_TRICOTAGE", 5, false)],
            cancellationToken);
        await EnsureCompiledAsync("DEMO_PF", ApsSegments.B, "CIR_REMAIL_INT",
            [new CompileBomLineInput("PIECE-DEMO", 1.0, "REMAILLAGE")],
            [new CompileRoutingOpInput("REMAILLAGE", "CC_REMAILLAGE", 12, true)],
            cancellationToken);
        await EnsureCompiledAsync("DEMO_PF", ApsSegments.C, "CIR_EXPED",
            [new CompileBomLineInput("DEMO_PF", 1.0, "EXPED")],
            [new CompileRoutingOpInput("EXPED", "CC_EXPED", 1, false)],
            cancellationToken);

        await using var connection = schema.OpenConnection();
        await Exec(connection, """
            IF NOT EXISTS (SELECT 1 FROM articles WHERE code = 'FIL-DEMO')
            BEGIN
                DECLARE @fam INT = (SELECT TOP 1 id FROM article_families);
                IF @fam IS NOT NULL
                BEGIN
                    INSERT INTO articles (family_id, code, label, article_type, default_unit, status, source_system, creation_mode,
                                          aps_decoupling_point, aps_traceability)
                    VALUES
                        (@fam, 'FIL-DEMO', 'Fil demo APS SIMULE', 'COMPONENT', 'KG', 'ACTIVE', 'APS_SEED', 'IMPORTED', 'FIL', 'BAIN'),
                        (@fam, 'PIECE-DEMO', 'Piece tricotee demo SIMULE', 'SEMI_FINISHED', 'UN', 'ACTIVE', 'APS_SEED', 'IMPORTED', 'PIECE_TRICOTEE', 'LOT'),
                        (@fam, 'DEMO_PF', 'Produit fini demo APS SIMULE', 'FINISHED_GOOD', 'UN', 'ACTIVE', 'APS_SEED', 'IMPORTED', 'PF', 'AUCUNE');
                END
            END
            """, cancellationToken);

        await Exec(connection, """
            IF NOT EXISTS (SELECT 1 FROM aps_stock_lots s JOIN articles a ON a.id = s.article_id WHERE a.code = 'FIL-DEMO')
            BEGIN
                DECLARE @fil INT = (SELECT id FROM articles WHERE code = 'FIL-DEMO');
                DECLARE @piece INT = (SELECT id FROM articles WHERE code = 'PIECE-DEMO');
                DECLARE @pf INT = (SELECT id FROM articles WHERE code = 'DEMO_PF');
                DECLARE @zone INT = (SELECT TOP 1 id FROM aps_stock_zones);
                INSERT INTO aps_stock_lots (article_id, lot_code, bath_code, quantity, unit, status, stock_zone_id) VALUES
                    (@fil, 'L-A', 'BAIN-A', 40, 'KG', 'LIBRE', @zone),
                    (@fil, 'L-B', 'BAIN-B', 35, 'KG', 'LIBRE', @zone),
                    (@fil, 'L-MTS', 'BAIN-MTS', 20, 'KG', 'RESERVE_MTS', @zone),
                    (@piece, 'LP1', NULL, 100, 'UN', 'LIBRE', @zone),
                    (@pf, 'LPF', NULL, 50, 'UN', 'LIBRE', @zone);
            END
            """, cancellationToken);
    }

    private async Task EnsureCompiledAsync(
        string root,
        string segment,
        string chain,
        CompileBomLineInput[] bom,
        CompileRoutingOpInput[] routing,
        CancellationToken cancellationToken)
    {
        var existing = await compilerRepository.ListArtifactsAsync(root, ApsCompilerStatuses.Valid, cancellationToken);
        if (existing.Any(a => a.Segment == segment && a.CircuitChain == chain && a.ArtifactKind == ApsCompilerArtifactKinds.Needs))
        {
            return;
        }

        var service = new ApsCompilerService(compilerRepository);
        await service.CompileAsync(new CompileApsRequest(
            root, segment, chain,
            BomFingerprint: $"seed-{root}-{segment}",
            RoutingFingerprint: $"seed-rtg-{segment}",
            BomLines: bom,
            RoutingOps: routing), cancellationToken);
    }

    public async Task<(bool Valid, string Hash, IReadOnlyList<ApsCompiledNeed> Needs)> LoadValidNeedsAsync(
        string rootArticleCode,
        string segment,
        string circuitChain,
        CancellationToken cancellationToken = default)
    {
        await schema.EnsureAsync(cancellationToken);
        var artifacts = await compilerRepository.ListArtifactsAsync(rootArticleCode, null, cancellationToken);
        var needsArtifact = artifacts
            .Where(a => a.Segment == segment
                        && a.CircuitChain == circuitChain
                        && a.ArtifactKind == ApsCompilerArtifactKinds.Needs)
            .OrderByDescending(a => a.CompiledAtUtc)
            .FirstOrDefault();

        if (needsArtifact is null)
        {
            return (false, string.Empty, []);
        }

        if (!string.Equals(needsArtifact.Status, ApsCompilerStatuses.Valid, StringComparison.OrdinalIgnoreCase))
        {
            return (false, needsArtifact.SourceHash, []);
        }

        var parsed = JsonSerializer.Deserialize<List<ApsNeedLineDto>>(needsArtifact.PayloadJson) ?? [];
        var needs = parsed.Select(p => new ApsCompiledNeed(
            p.ComponentCode,
            p.Quantity,
            p.OffsetDays,
            p.BathConstraint,
            p.SourcePath)).ToList();
        return (true, needsArtifact.SourceHash, needs);
    }

    private sealed record ApsNeedLineDto(
        string ComponentCode,
        double Quantity,
        double OffsetDays,
        string BathConstraint,
        string SourcePath,
        string ConfirmationStatus);

    public async Task<IReadOnlyList<ApsStockPosition>> LoadStockAsync(
        IReadOnlyList<string> articleCodes,
        CancellationToken cancellationToken = default)
    {
        await schema.EnsureAsync(cancellationToken);
        if (articleCodes.Count == 0)
        {
            return [];
        }

        await using var connection = schema.OpenConnection();
        await using var cmd = connection.CreateCommand();
        var inParams = string.Join(",", articleCodes.Select((_, i) => "@c" + i));
        cmd.CommandText = $"""
            SELECT a.code, s.status, s.quantity, s.bath_code
            FROM aps_stock_lots s
            JOIN articles a ON a.id = s.article_id
            WHERE a.code IN ({inParams})
            """;
        for (var i = 0; i < articleCodes.Count; i++)
        {
            cmd.Parameters.AddWithValue("@c" + i, articleCodes[i]);
        }

        var list = new List<ApsStockPosition>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new ApsStockPosition(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetDouble(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                null));
        }

        return list;
    }

    public async Task<long> PersistRunAsync(ApsSegmentCbnResult result, string artifactHash, CancellationToken cancellationToken = default)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var connection = schema.OpenConnection();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO aps_segment_cbn_runs (demand_id, segment, finished_article_code, success, error_message, artifact_hash, result_json)
            OUTPUT INSERTED.id
            VALUES (@d, @s, @a, @ok, @err, @hash, @json)
            """;
        cmd.Parameters.AddWithValue("@d", result.DemandId);
        cmd.Parameters.AddWithValue("@s", result.Segment);
        cmd.Parameters.AddWithValue("@a", result.FinishedArticleCode);
        cmd.Parameters.AddWithValue("@ok", result.Success);
        cmd.Parameters.AddWithValue("@err", (object?)result.Error ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@hash", (object?)artifactHash ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@json", JsonSerializer.Serialize(result));
        return Convert.ToInt64(await cmd.ExecuteScalarAsync(cancellationToken) ?? 0L);
    }

    public async Task<IReadOnlyList<ApsSegmentCbnRunHistoryDto>> ListRunHistoryAsync(int take = 20, CancellationToken cancellationToken = default)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var connection = schema.OpenConnection();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT TOP (@t) id, demand_id, segment, finished_article_code, success, created_at, result_json
            FROM aps_segment_cbn_runs
            ORDER BY id DESC
            """;
        cmd.Parameters.AddWithValue("@t", take);
        var list = new List<ApsSegmentCbnRunHistoryDto>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var parsed = JsonSerializer.Deserialize<ApsSegmentCbnResult>(reader.GetString(6));
            if (parsed is null)
            {
                continue;
            }

            list.Add(new ApsSegmentCbnRunHistoryDto(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetBoolean(4),
                reader.GetDateTime(5),
                parsed));
        }

        return list;
    }

    private static async Task Exec(SqlConnection connection, string sql, CancellationToken ct)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
