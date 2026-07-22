using System.Text.Json;
using Axioplan.GammesNomenclatures.Application.Abstractions;
using Axioplan.GammesNomenclatures.Application.Models;
using Axioplan.GammesNomenclatures.Domain.Aps.Contracts;
using Axioplan.GammesNomenclatures.Infrastructure.Aps;
using Microsoft.Data.SqlClient;

namespace Axioplan.GammesNomenclatures.Infrastructure.Repositories;

public sealed class SqlServerApsPhase9Repository(ApsSchemaBootstrap schema) : IApsPhase9Repository
{
    public Task EnsureSchemaAsync(CancellationToken cancellationToken = default)
        => schema.EnsureAsync(cancellationToken);

    public async Task SeedDemoAsync(CancellationToken cancellationToken = default)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var c = schema.OpenConnection();
        await Exec(c, """
            IF NOT EXISTS (SELECT 1 FROM aps_barrier_policies)
            INSERT INTO aps_barrier_policies (frozen_start, frozen_end, negociable_start, negociable_end)
            VALUES (0, 1, 2, 4);
            """, cancellationToken);
        await Exec(c, """
            IF NOT EXISTS (SELECT 1 FROM aps_inertia_parameters)
            INSERT INTO aps_inertia_parameters
                (date_w, load_w, composition_w, circuit_w, sequence_w, repromise_w, reservation_w)
            VALUES (10, 2, 15, 20, 8, 25, 12);
            """, cancellationToken);
        await Exec(c, """
            IF NOT EXISTS (SELECT 1 FROM aps_estimators WHERE code = 'eta_disponibilite')
            INSERT INTO aps_estimators (code, value, observation_count, source, as_of_utc, confidence, status)
            VALUES
                ('eta_disponibilite', 0.90, 3, 'SIMULE', SYSUTCDATETIME(), 'TO_CONFIRM', 'STABLE'),
                ('rho_cible', 0.85, 10, 'SIMULE', SYSUTCDATETIME(), 'MOYENNE', 'STABLE'),
                ('mu_fournisseur', NULL, 0, 'SIMULE', SYSUTCDATETIME(), 'TO_CONFIRM', 'MISSING');
            """, cancellationToken);
    }

    public async Task<ApsBarrierPolicy> GetBarrierPolicyAsync(CancellationToken cancellationToken = default)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var c = schema.OpenConnection();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT TOP 1 frozen_start, frozen_end, negociable_start, negociable_end FROM aps_barrier_policies";
        await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await r.ReadAsync(cancellationToken))
        {
            return BarrierPolicyEngine.Default;
        }

        return new ApsBarrierPolicy(r.GetInt32(0), r.GetInt32(1), r.GetInt32(2), r.GetInt32(3));
    }

    public async Task<ApsInertiaWeights> GetInertiaWeightsAsync(CancellationToken cancellationToken = default)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var c = schema.OpenConnection();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT TOP 1 date_w, load_w, composition_w, circuit_w, sequence_w, repromise_w, reservation_w FROM aps_inertia_parameters";
        await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await r.ReadAsync(cancellationToken))
        {
            return new ApsInertiaWeights(10, 2, 15, 20, 8, 25, 12);
        }

        return new ApsInertiaWeights(r.GetDouble(0), r.GetDouble(1), r.GetDouble(2), r.GetDouble(3), r.GetDouble(4), r.GetDouble(5), r.GetDouble(6));
    }

    public async Task<ApsFlowContract> SaveContractAsync(ApsFlowContract contract, CancellationToken cancellationToken = default)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var c = schema.OpenConnection();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = """
            INSERT INTO aps_flow_contracts
                (contract_id, version, status, iso_week, year, engaged_load, bottleneck_unit, window_start, window_end,
                 composition_json, envelopes_json, article_code, family_code, order_code, plan_ref, source_hash, published_at)
            VALUES (@id, @v, @st, @w, @y, @load, @u, @from, @to, @comp, @env, @a, @f, @o, @plan, @hash, @pub);

            INSERT INTO aps_flow_contract_versions (contract_id, version, status, payload_json, created_at)
            VALUES (@id, @v, @st, @payload, SYSUTCDATETIME());
            """;
        cmd.Parameters.AddWithValue("@id", contract.ContractId);
        cmd.Parameters.AddWithValue("@v", contract.Version);
        cmd.Parameters.AddWithValue("@st", contract.Status);
        cmd.Parameters.AddWithValue("@w", contract.IsoWeek);
        cmd.Parameters.AddWithValue("@y", contract.Year);
        cmd.Parameters.AddWithValue("@load", contract.EngagedLoad);
        cmd.Parameters.AddWithValue("@u", contract.BottleneckUnit);
        cmd.Parameters.AddWithValue("@from", contract.WindowStart.ToDateTime(TimeOnly.MinValue));
        cmd.Parameters.AddWithValue("@to", contract.WindowEnd.ToDateTime(TimeOnly.MinValue));
        cmd.Parameters.AddWithValue("@comp", JsonSerializer.Serialize(contract.Composition));
        cmd.Parameters.AddWithValue("@env", JsonSerializer.Serialize(contract.Envelopes));
        cmd.Parameters.AddWithValue("@a", (object?)contract.ArticleCode ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@f", (object?)contract.FamilyCode ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@o", (object?)contract.OrderCode ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@plan", contract.PlanRef);
        cmd.Parameters.AddWithValue("@hash", contract.SourceHash);
        cmd.Parameters.AddWithValue("@pub", contract.PublishedAtUtc);
        cmd.Parameters.AddWithValue("@payload", JsonSerializer.Serialize(contract));
        await cmd.ExecuteNonQueryAsync(cancellationToken);
        return contract;
    }

    public async Task<ApsFlowContract?> GetLatestContractAsync(string contractId, CancellationToken cancellationToken = default)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var c = schema.OpenConnection();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = """
            SELECT TOP 1 contract_id, version, status, iso_week, year, engaged_load, bottleneck_unit,
                   window_start, window_end, composition_json, envelopes_json, article_code, family_code,
                   order_code, plan_ref, source_hash, published_at
            FROM aps_flow_contracts WHERE contract_id=@id ORDER BY version DESC
            """;
        cmd.Parameters.AddWithValue("@id", contractId);
        await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await r.ReadAsync(cancellationToken))
        {
            return null;
        }

        return ReadContract(r);
    }

    public async Task<IReadOnlyList<ApsFlowContractDto>> ListContractsAsync(CancellationToken cancellationToken = default)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var c = schema.OpenConnection();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = """
            SELECT TOP 50 contract_id, version, status, iso_week, year, engaged_load, bottleneck_unit,
                   window_start, window_end, composition_json, plan_ref, source_hash, published_at
            FROM aps_flow_contracts ORDER BY id DESC
            """;
        var list = new List<ApsFlowContractDto>();
        await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await r.ReadAsync(cancellationToken))
        {
            list.Add(new ApsFlowContractDto(
                r.GetString(0), r.GetInt32(1), r.GetString(2), r.GetInt32(3), r.GetInt32(4),
                r.GetDouble(5), r.GetString(6),
                DateOnly.FromDateTime(r.GetDateTime(7)), DateOnly.FromDateTime(r.GetDateTime(8)),
                r.GetString(9), r.GetString(10), r.GetString(11), r.GetDateTime(12)));
        }

        return list;
    }

    private static ApsFlowContract ReadContract(SqlDataReader r)
    {
        var comp = JsonSerializer.Deserialize<List<ApsCompositionShare>>(r.GetString(9)) ?? [];
        var env = JsonSerializer.Deserialize<List<ApsResourceEnvelope>>(r.GetString(10)) ?? [];
        return new ApsFlowContract(
            r.GetString(0), r.GetInt32(1), r.GetString(2), r.GetInt32(3), r.GetInt32(4),
            r.GetDouble(5), r.GetString(6),
            DateOnly.FromDateTime(r.GetDateTime(7)), DateOnly.FromDateTime(r.GetDateTime(8)),
            comp, env,
            r.IsDBNull(11) ? null : r.GetString(11),
            r.IsDBNull(12) ? null : r.GetString(12),
            r.IsDBNull(13) ? null : r.GetString(13),
            r.GetString(14), r.GetDateTime(16), r.GetString(15));
    }

    public async Task SavePlanSnapshotAsync(
        string grain, string roleOrPost, DateOnly dayOrWeekStart, string payloadJson, string planRef,
        CancellationToken cancellationToken = default)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var c = schema.OpenConnection();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = """
            INSERT INTO aps_plan_snapshots (grain, role_or_post, start_date, payload_json, plan_ref)
            VALUES (@g, @r, @d, @p, @plan);
            INSERT INTO aps_plan_projection_lines (grain, role_or_post, start_date, line_json, plan_ref)
            VALUES (@g, @r, @d, @p, @plan);
            """;
        cmd.Parameters.AddWithValue("@g", grain);
        cmd.Parameters.AddWithValue("@r", roleOrPost);
        cmd.Parameters.AddWithValue("@d", dayOrWeekStart.ToDateTime(TimeOnly.MinValue));
        cmd.Parameters.AddWithValue("@p", payloadJson);
        cmd.Parameters.AddWithValue("@plan", planRef);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<(string Grain, string RoleOrPost, DateOnly Start, string PayloadJson, string PlanRef)>> ListPlanProjectionsAsync(
        string? grain, CancellationToken cancellationToken = default)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var c = schema.OpenConnection();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = grain is null
            ? "SELECT TOP 50 grain, role_or_post, start_date, payload_json, plan_ref FROM aps_plan_snapshots ORDER BY id DESC"
            : "SELECT TOP 50 grain, role_or_post, start_date, payload_json, plan_ref FROM aps_plan_snapshots WHERE grain=@g ORDER BY id DESC";
        if (grain is not null)
        {
            cmd.Parameters.AddWithValue("@g", grain);
        }

        var list = new List<(string, string, DateOnly, string, string)>();
        await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await r.ReadAsync(cancellationToken))
        {
            list.Add((r.GetString(0), r.GetString(1), DateOnly.FromDateTime(r.GetDateTime(2)), r.GetString(3), r.GetString(4)));
        }

        return list;
    }

    public async Task UpsertEstimatorAsync(ApsEstimatorState state, CancellationToken cancellationToken = default)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var c = schema.OpenConnection();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = """
            MERGE aps_estimators AS t
            USING (SELECT @c AS code) AS s ON t.code = s.code
            WHEN MATCHED THEN UPDATE SET value=@v, observation_count=@n, source=@src, as_of_utc=@at, confidence=@conf, status=@st
            WHEN NOT MATCHED THEN INSERT (code, value, observation_count, source, as_of_utc, confidence, status)
                VALUES (@c, @v, @n, @src, @at, @conf, @st);
            """;
        cmd.Parameters.AddWithValue("@c", state.Code);
        cmd.Parameters.AddWithValue("@v", (object?)state.Value ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@n", state.ObservationCount);
        cmd.Parameters.AddWithValue("@src", state.Source);
        cmd.Parameters.AddWithValue("@at", state.AsOfUtc);
        cmd.Parameters.AddWithValue("@conf", state.Confidence);
        cmd.Parameters.AddWithValue("@st", state.Status);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ApsEstimatorState>> ListEstimatorsAsync(CancellationToken cancellationToken = default)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var c = schema.OpenConnection();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT code, value, observation_count, source, as_of_utc, confidence, status FROM aps_estimators";
        var list = new List<ApsEstimatorState>();
        await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await r.ReadAsync(cancellationToken))
        {
            list.Add(new ApsEstimatorState(
                r.GetString(0), r.IsDBNull(1) ? null : r.GetDouble(1), r.GetInt32(2),
                r.GetString(3), r.GetDateTime(4), r.GetString(5), r.GetString(6)));
        }

        return list;
    }

    public async Task SaveRecalibrationAsync(ApsRecalibrationResult result, CancellationToken cancellationToken = default)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var c = schema.OpenConnection();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = """
            INSERT INTO aps_recalibration_history
                (estimator_code, old_value, new_value, observation_count, source, as_of_utc, confidence, status, reason, relative_change, invalidates)
            VALUES (@c, @o, @n, @cnt, @src, @at, @conf, @st, @reason, @rel, @inv);
            INSERT INTO aps_estimator_observations (estimator_code, observed_value, observed_at)
            VALUES (@c, @n, @at);
            """;
        cmd.Parameters.AddWithValue("@c", result.EstimatorCode);
        cmd.Parameters.AddWithValue("@o", (object?)result.OldValue ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@n", (object?)result.NewValue ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@cnt", result.ObservationCount);
        cmd.Parameters.AddWithValue("@src", result.Source);
        cmd.Parameters.AddWithValue("@at", result.AsOfUtc);
        cmd.Parameters.AddWithValue("@conf", result.Confidence);
        cmd.Parameters.AddWithValue("@st", result.Status);
        cmd.Parameters.AddWithValue("@reason", result.Reason);
        cmd.Parameters.AddWithValue("@rel", result.RelativeChange);
        cmd.Parameters.AddWithValue("@inv", result.InvalidatesArtifacts);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public Task<(bool Ok, string Hash, string Status)> InvalidateAndRecompileDemoAsync(
        string rootArticle, bool forceFail, CancellationToken cancellationToken = default)
    {
        if (forceFail)
        {
            return Task.FromResult((false, string.Empty, "INVALID"));
        }

        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(rootArticle + DateTime.UtcNow.ToString("yyyyMMdd"))));
        return Task.FromResult((true, hash, "VALID"));
    }

    public async Task<long> StartNightlyRunAsync(CancellationToken cancellationToken = default)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var c = schema.OpenConnection();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = """
            INSERT INTO aps_nightly_runs (started_at, status) OUTPUT INSERTED.id
            VALUES (SYSUTCDATETIME(), 'RUNNING')
            """;
        return Convert.ToInt64(await cmd.ExecuteScalarAsync(cancellationToken));
    }

    public async Task AddNightlyStepAsync(long runId, int order, string name, string status, string detail, CancellationToken cancellationToken = default)
    {
        await using var c = schema.OpenConnection();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = """
            INSERT INTO aps_nightly_run_steps (run_id, step_order, step_name, status, detail, at_utc)
            VALUES (@r, @o, @n, @s, @d, SYSUTCDATETIME())
            """;
        cmd.Parameters.AddWithValue("@r", runId);
        cmd.Parameters.AddWithValue("@o", order);
        cmd.Parameters.AddWithValue("@n", name);
        cmd.Parameters.AddWithValue("@s", status);
        cmd.Parameters.AddWithValue("@d", detail);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task FinishNightlyRunAsync(long runId, string status, string? notes, CancellationToken cancellationToken = default)
    {
        await using var c = schema.OpenConnection();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = "UPDATE aps_nightly_runs SET finished_at=SYSUTCDATETIME(), status=@s, notes=@n WHERE id=@id";
        cmd.Parameters.AddWithValue("@s", status);
        cmd.Parameters.AddWithValue("@n", (object?)notes ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@id", runId);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ApsNightlyRunDto>> ListNightlyRunsAsync(CancellationToken cancellationToken = default)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var c = schema.OpenConnection();

        // 1) Lire tous les runs et fermer le reader avant toute autre requête
        //    (évite "There is already an open DataReader associated with this Connection").
        var runs = new List<ApsNightlyRunDto>();
        await using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = "SELECT TOP 20 id, started_at, finished_at, status, notes FROM aps_nightly_runs ORDER BY id DESC";
            await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await r.ReadAsync(cancellationToken))
            {
                runs.Add(new ApsNightlyRunDto(
                    r.GetInt64(0), r.GetDateTime(1),
                    r.IsDBNull(2) ? null : r.GetDateTime(2),
                    r.GetString(3), r.IsDBNull(4) ? null : r.GetString(4), []));
            }
        }

        // 2) Charger les steps run par run, un reader à la fois
        var result = new List<ApsNightlyRunDto>(runs.Count);
        foreach (var run in runs)
        {
            var steps = new List<ApsNightlyStepDto>();
            await using (var sCmd = c.CreateCommand())
            {
                sCmd.CommandText = "SELECT step_order, step_name, status, detail, at_utc FROM aps_nightly_run_steps WHERE run_id=@id ORDER BY step_order";
                sCmd.Parameters.AddWithValue("@id", run.Id);
                await using var sr = await sCmd.ExecuteReaderAsync(cancellationToken);
                while (await sr.ReadAsync(cancellationToken))
                {
                    steps.Add(new ApsNightlyStepDto(
                        sr.GetInt32(0), sr.GetString(1), sr.GetString(2), sr.GetString(3), sr.GetDateTime(4)));
                }
            }

            result.Add(run with { Steps = steps });
        }

        return result;
    }

    public async Task SaveMatchesAsync(IReadOnlyList<ApsExpectationFactMatch> matches, CancellationToken cancellationToken = default)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var c = schema.OpenConnection();
        foreach (var m in matches)
        {
            await using var cmd = c.CreateCommand();
            cmd.CommandText = """
                INSERT INTO aps_expectation_fact_matches
                    (expected_id, event_id, dimension, magnitude, hre_impacted, gauge_zone, occurred_at, detected_at)
                VALUES (@e, @ev, @d, @m, @h, @g, @o, @det)
                """;
            cmd.Parameters.AddWithValue("@e", (object?)m.ExpectedId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ev", (object?)m.EventId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@d", m.Dimension);
            cmd.Parameters.AddWithValue("@m", m.Magnitude);
            cmd.Parameters.AddWithValue("@h", (object?)m.HreImpacted ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@g", m.GaugeZone);
            cmd.Parameters.AddWithValue("@o", (object?)m.OccurredAtUtc ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@det", m.DetectedAtUtc);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    public async Task SaveReplayAsync(ApsReplayValidationResult result, CancellationToken cancellationToken = default)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var c = schema.OpenConnection();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = """
            INSERT INTO aps_replay_validations (status, global_score, unlock_m2_m6, detail_json)
            VALUES (@s, @g, @u, @d)
            """;
        cmd.Parameters.AddWithValue("@s", result.Status);
        cmd.Parameters.AddWithValue("@g", (object?)result.GlobalScore ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@u", result.UnlockM2M6);
        cmd.Parameters.AddWithValue("@d", JsonSerializer.Serialize(result));
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ApsReplayDto>> ListReplaysAsync(CancellationToken cancellationToken = default)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var c = schema.OpenConnection();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT TOP 20 id, status, global_score, unlock_m2_m6, detail_json, created_at FROM aps_replay_validations ORDER BY id DESC";
        var list = new List<ApsReplayDto>();
        await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await r.ReadAsync(cancellationToken))
        {
            list.Add(new ApsReplayDto(
                r.GetInt64(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetDouble(2),
                r.GetBoolean(3), r.GetString(4), r.GetDateTime(5)));
        }

        return list;
    }

    public async Task SaveNervousnessAsync(DateOnly from, DateOnly to, ApsNervousnessMetrics metrics, CancellationToken cancellationToken = default)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var c = schema.OpenConnection();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = """
            INSERT INTO aps_nervousness_metrics (period_from, period_to, composite_index, metrics_json)
            VALUES (@f, @t, @c, @j)
            """;
        cmd.Parameters.AddWithValue("@f", from.ToDateTime(TimeOnly.MinValue));
        cmd.Parameters.AddWithValue("@t", to.ToDateTime(TimeOnly.MinValue));
        cmd.Parameters.AddWithValue("@c", metrics.CompositeIndex);
        cmd.Parameters.AddWithValue("@j", JsonSerializer.Serialize(metrics));
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ApsNervousnessDto>> ListNervousnessAsync(CancellationToken cancellationToken = default)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var c = schema.OpenConnection();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT TOP 20 id, period_from, period_to, composite_index, metrics_json, created_at FROM aps_nervousness_metrics ORDER BY id DESC";
        var list = new List<ApsNervousnessDto>();
        await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await r.ReadAsync(cancellationToken))
        {
            list.Add(new ApsNervousnessDto(
                r.GetInt64(0),
                DateOnly.FromDateTime(r.GetDateTime(1)),
                DateOnly.FromDateTime(r.GetDateTime(2)),
                r.GetDouble(3), r.GetString(4), r.GetDateTime(5)));
        }

        return list;
    }

    public async Task<ApsReplayValidationResult?> GetLatestRecipeAsync(CancellationToken cancellationToken = default)
    {
        var list = await ListReplaysAsync(cancellationToken);
        if (list.Count == 0)
        {
            return null;
        }

        var dto = list[0];
        return new ApsReplayValidationResult(
            dto.GlobalScore,
            new Dictionary<string, double>(),
            [],
            dto.Status,
            dto.UnlockM2M6);
    }

    private static async Task Exec(SqlConnection c, string sql, CancellationToken ct)
    {
        await using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
