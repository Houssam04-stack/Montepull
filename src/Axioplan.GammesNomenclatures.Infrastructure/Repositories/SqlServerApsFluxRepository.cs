using System.Text.Json;
using Axioplan.GammesNomenclatures.Application.Abstractions;
using Axioplan.GammesNomenclatures.Application.Aps;
using Axioplan.GammesNomenclatures.Application.Models;
using Axioplan.GammesNomenclatures.Domain.Aps.Compiler;
using Axioplan.GammesNomenclatures.Domain.Aps.Flux;
using Axioplan.GammesNomenclatures.Domain.Aps.SegmentCbn;
using Axioplan.GammesNomenclatures.Infrastructure.Aps;
using Microsoft.Data.SqlClient;

namespace Axioplan.GammesNomenclatures.Infrastructure.Repositories;

public sealed class SqlServerApsFluxRepository(
    ApsSchemaBootstrap schema,
    IApsCompilerRepository compilerRepository) : IApsFluxRepository
{
    public Task EnsureSchemaAsync(CancellationToken cancellationToken = default)
        => schema.EnsureAsync(cancellationToken);

    public async Task SeedDemoAsync(CancellationToken cancellationToken = default)
    {
        await schema.EnsureAsync(cancellationToken);
        await compilerRepository.EnsureSchemaAsync(cancellationToken);

        // Artefacts CHARGES VALID pour DEMO_PF (réutilise compiler si absent)
        await EnsureChargesAsync("DEMO_PF", ApsSegments.A, "CIR_TRICOT_INT",
            [new CompileRoutingOpInput("TRICOTAGE", "CC_TRICOTAGE", 5, false)], cancellationToken);
        await EnsureChargesAsync("DEMO_PF", ApsSegments.B, "CIR_REMAIL_INT",
            [new CompileRoutingOpInput("REMAILLAGE", "CC_REMAILLAGE", 12, true)], cancellationToken);
        await EnsureChargesAsync("DEMO_PF", ApsSegments.C, "CIR_EXPED",
            [new CompileRoutingOpInput("EXPED", "CC_EXPED", 1, false)], cancellationToken);

        await using var connection = schema.OpenConnection();

        await Exec(connection, """
            IF NOT EXISTS (SELECT 1 FROM aps_space_capacity WHERE place_code = 'ZONE_AMONT_REMAIL')
            INSERT INTO aps_space_capacity (place_code, occupied_volume, max_volume, confirmation_status)
            VALUES
                ('ZONE_AMONT_REMAIL', 75, 100, 'TO_CONFIRM'),
                ('ZONE_TRICOT_WIP', 40, 80, 'TO_CONFIRM');
            """, cancellationToken);

        // k / sigma absents => TO_CONFIRM explicite (ne pas inventer sigma)
        await Exec(connection, """
            IF NOT EXISTS (SELECT 1 FROM aps_buffer_targets WHERE charge_center_code = 'CC_REMAILLAGE')
            INSERT INTO aps_buffer_targets (charge_center_code, k_factor, reaction_lead_days, variance_json, confirmation_status)
            VALUES ('CC_REMAILLAGE', NULL, NULL, NULL, 'TO_CONFIRM');
            """, cancellationToken);

        // Pas de mix attendu par défaut → adéquation TO_CONFIRM
        // HRE demo sur articles
        await Exec(connection, """
            UPDATE articles SET aps_hre_precompile = 0.2 WHERE code = 'PIECE-DEMO' AND aps_hre_precompile IS NULL;
            UPDATE articles SET aps_hre_precompile = 0.05 WHERE code = 'DEMO_PF' AND aps_hre_precompile IS NULL;
            """, cancellationToken);

        // Ne pas inserer dans aps_bottleneck_history au seed (pas de GoulotDeplace).
    }

    private async Task EnsureChargesAsync(
        string root,
        string segment,
        string chain,
        CompileRoutingOpInput[] routing,
        CancellationToken cancellationToken)
    {
        var existing = await compilerRepository.ListArtifactsAsync(root, ApsCompilerStatuses.Valid, cancellationToken);
        if (existing.Any(a => a.Segment == segment && a.CircuitChain == chain && a.ArtifactKind == ApsCompilerArtifactKinds.Loads))
        {
            return;
        }

        var service = new ApsCompilerService(compilerRepository);
        await service.CompileAsync(new CompileApsRequest(
            root, segment, chain,
            BomFingerprint: $"flux-seed-{root}-{segment}",
            RoutingFingerprint: $"flux-rtg-{segment}",
            BomLines: [new CompileBomLineInput(root == "DEMO_PF" && segment == ApsSegments.A ? "FIL-DEMO" : "PIECE-DEMO", 1, "OP")],
            RoutingOps: routing), cancellationToken);
    }

    public async Task<(bool Valid, string Hash, IReadOnlyList<ApsCompiledLoadLine> Loads)> LoadValidChargesAsync(
        string rootArticleCode,
        string segment,
        string circuitChain,
        CancellationToken cancellationToken = default)
    {
        await schema.EnsureAsync(cancellationToken);
        var artifacts = await compilerRepository.ListArtifactsAsync(rootArticleCode, null, cancellationToken);
        var art = artifacts
            .Where(a => a.Segment == segment
                        && a.CircuitChain == circuitChain
                        && a.ArtifactKind == ApsCompilerArtifactKinds.Loads)
            .OrderByDescending(a => a.CompiledAtUtc)
            .FirstOrDefault();

        if (art is null)
        {
            return (false, string.Empty, []);
        }

        if (!string.Equals(art.Status, ApsCompilerStatuses.Valid, StringComparison.OrdinalIgnoreCase))
        {
            return (false, art.SourceHash, []);
        }

        var parsed = JsonSerializer.Deserialize<List<LoadDto>>(art.PayloadJson) ?? [];
        var unitFor = (string capacityType) => capacityType.ToUpperInvariant() switch
        {
            "LOT" => "BAINS",
            "MAIN_OEUVRE" => "HEURES_PERSONNE",
            "EXTERNE" => "NONE",
            _ => "HEURES_MACHINE"
        };

        // Align capacity type with known resource codes (compiler may mark MACHINE generically)
        var loads = parsed.Select(p =>
        {
            var type = GuessType(p.ResourceCode, p.CapacityType);
            return new ApsCompiledLoadLine(
                p.ResourceCode,
                type,
                p.UnitTime,
                p.SetupProvision,
                unitFor(type),
                true,
                art.SourceHash);
        }).ToList();

        return (true, art.SourceHash, loads);
    }

    private static string GuessType(string resourceCode, string capacityType)
    {
        if (resourceCode.Contains("TRAITEMENT", StringComparison.OrdinalIgnoreCase)
            || resourceCode.Contains("LOT", StringComparison.OrdinalIgnoreCase))
        {
            return ApsResourceAlgebras.Lot;
        }

        if (resourceCode.Contains("REMAIL", StringComparison.OrdinalIgnoreCase)
            || resourceCode.Contains("REPASS", StringComparison.OrdinalIgnoreCase)
            || resourceCode.Contains("EMBALL", StringComparison.OrdinalIgnoreCase))
        {
            return ApsResourceAlgebras.Labour;
        }

        if (capacityType.Equals(ApsResourceAlgebras.External, StringComparison.OrdinalIgnoreCase))
        {
            return ApsResourceAlgebras.External;
        }

        return string.IsNullOrWhiteSpace(capacityType) ? ApsResourceAlgebras.Machine : capacityType;
    }

    private sealed record LoadDto(
        string ResourceCode,
        string CapacityType,
        double UnitTime,
        double SetupProvision,
        string ConfirmationStatus);

    public async Task<IReadOnlyList<ApsLaunchQuantity>> ResolveLaunchesFromCbnRunAsync(
        long? segmentCbnRunId,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default)
    {
        if (segmentCbnRunId is null)
        {
            return [];
        }

        await schema.EnsureAsync(cancellationToken);
        await using var connection = schema.OpenConnection();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT result_json FROM aps_segment_cbn_runs WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", segmentCbnRunId.Value);
        var json = (string?)await cmd.ExecuteScalarAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        var run = JsonSerializer.Deserialize<ApsSegmentCbnResult>(json);
        if (run is null)
        {
            return [];
        }

        var circuit = ResolveCircuit(run.Segment);
        var (_, _, loadLines) = await LoadValidChargesAsync(
            run.FinishedArticleCode,
            run.Segment,
            circuit,
            cancellationToken);

        var launches = new List<ApsLaunchQuantity>();
        var launchLines = run.Lines.Where(l => l.QtyToLaunch > 0).ToList();
        if (launchLines.Count == 0)
        {
            return launches;
        }

        var daySpan = Math.Max(1, to.DayNumber - from.DayNumber + 1);
        var index = 0;
        foreach (var line in launchLines)
        {
            var resource = ResolveResourceForLine(run.Segment, line.ArticleCode, loadLines);
            var offset = index % daySpan;
            var bucketDate = from.AddDays(offset);
            if (bucketDate > to)
            {
                bucketDate = to;
            }

            launches.Add(new ApsLaunchQuantity(line.ArticleCode, resource, bucketDate, line.QtyToLaunch));
            index++;
        }

        return launches;
    }

    private static string ResolveCircuit(string segment)
        => segment switch
        {
            "SEG_A" => "CIR_TRICOT_INT",
            "SEG_B" => "CIR_REMAIL_INT",
            _ => "CIR_EXPED"
        };

    private static string ResolveResourceForLine(
        string segment,
        string articleCode,
        IReadOnlyList<ApsCompiledLoadLine> loadLines)
    {
        if (loadLines.Count > 0)
        {
            var match = loadLines.FirstOrDefault(l =>
                articleCode.Contains("FIL", StringComparison.OrdinalIgnoreCase)
                    && l.ResourceCode.Contains("TRICOT", StringComparison.OrdinalIgnoreCase))
                ?? loadLines.FirstOrDefault(l =>
                    articleCode.Contains("PIECE", StringComparison.OrdinalIgnoreCase)
                    && l.ResourceCode.Contains("REMAIL", StringComparison.OrdinalIgnoreCase))
                ?? loadLines[0];
            return match.ResourceCode;
        }

        return segment switch
        {
            "SEG_A" => "CC_TRICOTAGE",
            "SEG_B" => "CC_REMAILLAGE",
            _ => "CC_REMAILLAGE"
        };
    }

    public async Task<IReadOnlyList<ApsBufferStockLine>> LoadUpstreamBufferStockAsync(
        string chargeCenterCode,
        CancellationToken cancellationToken = default)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var connection = schema.OpenConnection();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT a.code,
                   ISNULL(CAST(a.family_id AS NVARCHAR(40)), ''),
                   a.code,
                   NULL,
                   s.quantity,
                   ISNULL(a.aps_hre_precompile, 0.1),
                   ISNULL(z.role, 'AMONT')
            FROM aps_stock_lots s
            JOIN articles a ON a.id = s.article_id
            LEFT JOIN aps_stock_zones z ON z.id = s.stock_zone_id
            WHERE a.code IN ('PIECE-DEMO', 'DEMO_PF')
              AND (z.role = 'AMONT' OR z.role IS NULL OR @cc = 'CC_REMAILLAGE')
            """;
        cmd.Parameters.AddWithValue("@cc", chargeCenterCode);
        var list = new List<ApsBufferStockLine>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new ApsBufferStockLine(
                reader.GetString(0),
                reader.IsDBNull(1) ? null : reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                null,
                reader.GetDouble(4),
                reader.GetDouble(5),
                reader.IsDBNull(6) ? "AMONT" : reader.GetString(6)));
        }

        // Fallback SIMULE si stock zones manquantes
        if (list.Count == 0)
        {
            list.Add(new ApsBufferStockLine("PIECE-DEMO", "PULL", "M1", null, 100, 0.2, "AMONT"));
            list.Add(new ApsBufferStockLine("PIECE-DEMO", "PULL", "M2", null, 50, 0.2, "AMONT"));
        }
        else
        {
            // Forcer role AMONT pour buffer remaillage demo
            list = list.Select(l => l with { ZoneRole = "AMONT" }).ToList();
        }

        return list;
    }

    public async Task<IReadOnlyList<ApsExpectedMixLine>> LoadExpectedMixAsync(CancellationToken cancellationToken = default)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var connection = schema.OpenConnection();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT article_code, share_of_mix FROM aps_expected_mix";
        var list = new List<ApsExpectedMixLine>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new ApsExpectedMixLine(reader.GetString(0), reader.GetDouble(1)));
        }

        return list;
    }

    public async Task<(double? K, IReadOnlyList<double>? Variances, double? ReactionDays)> LoadBufferTargetParamsAsync(
        string chargeCenterCode,
        CancellationToken cancellationToken = default)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var connection = schema.OpenConnection();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT k_factor, reaction_lead_days, variance_json
            FROM aps_buffer_targets WHERE charge_center_code = @c
            """;
        cmd.Parameters.AddWithValue("@c", chargeCenterCode);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return (null, null, null);
        }

        double? k = reader.IsDBNull(0) ? null : reader.GetDouble(0);
        double? reaction = reader.IsDBNull(1) ? null : reader.GetDouble(1);
        IReadOnlyList<double>? variances = null;
        if (!reader.IsDBNull(2))
        {
            variances = JsonSerializer.Deserialize<List<double>>(reader.GetString(2));
        }

        return (k, variances, reaction);
    }

    public async Task<IReadOnlyList<(string PlaceCode, double Occupied, double Max)>> LoadSpaceCapacitiesAsync(
        CancellationToken cancellationToken = default)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var connection = schema.OpenConnection();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT place_code, occupied_volume, max_volume FROM aps_space_capacity";
        var list = new List<(string, double, double)>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add((reader.GetString(0), reader.GetDouble(1), reader.GetDouble(2)));
        }

        return list;
    }

    public async Task<ApsBottleneckResult?> GetLastBottleneckAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var connection = schema.OpenConnection();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT TOP 1 constraining_code, kind, rho, from_date, to_date, explanation
            FROM aps_bottleneck_history
            WHERE from_date = @f AND to_date = @t
            ORDER BY id DESC
            """;
        cmd.Parameters.AddWithValue("@f", from.ToDateTime(TimeOnly.MinValue));
        cmd.Parameters.AddWithValue("@t", to.ToDateTime(TimeOnly.MinValue));
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new ApsBottleneckResult(
            reader.GetString(0),
            reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetDouble(2),
            DateOnly.FromDateTime(reader.GetDateTime(3)),
            DateOnly.FromDateTime(reader.GetDateTime(4)),
            "HIST",
            0,
            0,
            null,
            [],
            reader.GetString(5));
    }

    public async Task SavePublishedRunAsync(ApsFluxComputeResultDto result, CancellationToken cancellationToken = default)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var connection = schema.OpenConnection();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO aps_load_runs (from_date, to_date, bottleneck_code, result_json, published)
            OUTPUT INSERTED.id
            VALUES (@f, @t, @bn, @json, 1)
            """;
        cmd.Parameters.AddWithValue("@f", result.From.ToDateTime(TimeOnly.MinValue));
        cmd.Parameters.AddWithValue("@t", result.To.ToDateTime(TimeOnly.MinValue));
        cmd.Parameters.AddWithValue("@bn", result.Bottleneck.ConstrainingCode);
        cmd.Parameters.AddWithValue("@json", JsonSerializer.Serialize(result));
        var runId = Convert.ToInt64(await cmd.ExecuteScalarAsync(cancellationToken));

        foreach (var line in result.Loads)
        {
            await using var lineCmd = connection.CreateCommand();
            lineCmd.CommandText = """
                INSERT INTO aps_load_lines (run_id, resource_code, bucket_date, capacity_type, unit, qty_to_launch, charge, traces)
                VALUES (@r, @res, @d, @ct, @u, @q, @c, @tr)
                """;
            lineCmd.Parameters.AddWithValue("@r", runId);
            lineCmd.Parameters.AddWithValue("@res", line.ResourceCode);
            lineCmd.Parameters.AddWithValue("@d", line.BucketDate.ToDateTime(TimeOnly.MinValue));
            lineCmd.Parameters.AddWithValue("@ct", line.CapacityType);
            lineCmd.Parameters.AddWithValue("@u", line.Unit);
            lineCmd.Parameters.AddWithValue("@q", line.QtyToLaunch);
            lineCmd.Parameters.AddWithValue("@c", line.Charge);
            lineCmd.Parameters.AddWithValue("@tr", string.Join(" | ", line.Traces));
            await lineCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var sat in result.Saturations)
        {
            await using var satCmd = connection.CreateCommand();
            satCmd.CommandText = """
                INSERT INTO aps_saturation_results (run_id, resource_code, from_date, to_date, charge_cum, cap_cum, rho, rho_target, status)
                VALUES (@r, @res, @f, @t, @ch, @cap, @rho, @rt, @st)
                """;
            satCmd.Parameters.AddWithValue("@r", runId);
            satCmd.Parameters.AddWithValue("@res", sat.ResourceCode);
            satCmd.Parameters.AddWithValue("@f", sat.From.ToDateTime(TimeOnly.MinValue));
            satCmd.Parameters.AddWithValue("@t", sat.To.ToDateTime(TimeOnly.MinValue));
            satCmd.Parameters.AddWithValue("@ch", sat.ChargeCumulee);
            satCmd.Parameters.AddWithValue("@cap", sat.CapCum);
            satCmd.Parameters.AddWithValue("@rho", (object?)sat.Rho ?? DBNull.Value);
            satCmd.Parameters.AddWithValue("@rt", sat.RhoTarget);
            satCmd.Parameters.AddWithValue("@st", sat.Status);
            await satCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var bufCmd = connection.CreateCommand();
        bufCmd.CommandText = """
            INSERT INTO aps_buffer_snapshots (charge_center_code, buffer_hre, coverage_days, adequacy, adequacy_status, zone)
            OUTPUT INSERTED.id
            VALUES (@cc, @hre, @cov, @ad, @ads, @zone)
            """;
        bufCmd.Parameters.AddWithValue("@cc", result.Buffer.ChargeCenterCode);
        bufCmd.Parameters.AddWithValue("@hre", result.Buffer.BufferHre);
        bufCmd.Parameters.AddWithValue("@cov", (object?)result.Buffer.CoverageDays ?? DBNull.Value);
        bufCmd.Parameters.AddWithValue("@ad", (object?)result.Buffer.Adequacy ?? DBNull.Value);
        bufCmd.Parameters.AddWithValue("@ads", result.Buffer.AdequacyStatus);
        bufCmd.Parameters.AddWithValue("@zone", result.BufferTarget.Zone);
        var snapId = Convert.ToInt64(await bufCmd.ExecuteScalarAsync(cancellationToken));

        foreach (var c in result.Buffer.Composition)
        {
            await using var cCmd = connection.CreateCommand();
            cCmd.CommandText = """
                INSERT INTO aps_buffer_composition (snapshot_id, article_code, family_code, quantity, hre_per_unit)
                VALUES (@s, @a, @f, @q, @h)
                """;
            cCmd.Parameters.AddWithValue("@s", snapId);
            cCmd.Parameters.AddWithValue("@a", c.ArticleCode);
            cCmd.Parameters.AddWithValue("@f", (object?)c.FamilyCode ?? DBNull.Value);
            cCmd.Parameters.AddWithValue("@q", c.Quantity);
            cCmd.Parameters.AddWithValue("@h", c.HrePerUnit);
            await cCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var ropeCmd = connection.CreateCommand();
        ropeCmd.CommandText = """
            INSERT INTO aps_rope_recommendations (recommended_qty, priority, justification, creates_wo, payload_json)
            VALUES (@q, @p, @j, 0, @json)
            """;
        ropeCmd.Parameters.AddWithValue("@q", result.Rope.RecommendedQty);
        ropeCmd.Parameters.AddWithValue("@p", result.Rope.Priority);
        ropeCmd.Parameters.AddWithValue("@j", result.Rope.Justification);
        ropeCmd.Parameters.AddWithValue("@json", JsonSerializer.Serialize(result.Rope));
        await ropeCmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task PersistBottleneckHistoryAsync(
        ApsBottleneckResult bn,
        bool moved,
        CancellationToken cancellationToken = default)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var connection = schema.OpenConnection();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO aps_bottleneck_history (from_date, to_date, constraining_code, kind, rho, explanation, moved)
            VALUES (@f, @t, @c, @k, @rho, @e, @m)
            """;
        cmd.Parameters.AddWithValue("@f", bn.From.ToDateTime(TimeOnly.MinValue));
        cmd.Parameters.AddWithValue("@t", bn.To.ToDateTime(TimeOnly.MinValue));
        cmd.Parameters.AddWithValue("@c", bn.ConstrainingCode);
        cmd.Parameters.AddWithValue("@k", bn.Kind);
        cmd.Parameters.AddWithValue("@rho", (object?)bn.Rho ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@e", bn.Explanation);
        cmd.Parameters.AddWithValue("@m", moved);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<(DateTime At, string Code, string Kind, string Explanation)>> ListBottleneckHistoryAsync(
        int take = 20,
        CancellationToken cancellationToken = default)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var connection = schema.OpenConnection();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT TOP (@t) created_at, constraining_code, kind, explanation
            FROM aps_bottleneck_history ORDER BY id DESC
            """;
        cmd.Parameters.AddWithValue("@t", take);
        var list = new List<(DateTime, string, string, string)>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add((reader.GetDateTime(0), reader.GetString(1), reader.GetString(2), reader.GetString(3)));
        }

        return list;
    }

    public async Task<IReadOnlyList<ApsLoadRunSummaryDto>> ListLoadRunsAsync(int take = 20, CancellationToken cancellationToken = default)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var connection = schema.OpenConnection();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT TOP (@t) id, from_date, to_date, created_at, bottleneck_code
            FROM aps_load_runs ORDER BY id DESC
            """;
        cmd.Parameters.AddWithValue("@t", take);
        var list = new List<ApsLoadRunSummaryDto>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new ApsLoadRunSummaryDto(
                reader.GetInt64(0),
                DateOnly.FromDateTime(reader.GetDateTime(1)),
                DateOnly.FromDateTime(reader.GetDateTime(2)),
                reader.GetDateTime(3),
                reader.IsDBNull(4) ? null : reader.GetString(4)));
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
