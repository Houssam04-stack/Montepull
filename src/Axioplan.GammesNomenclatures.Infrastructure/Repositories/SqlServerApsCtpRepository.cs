using System.Text.Json;
using Axioplan.GammesNomenclatures.Application.Abstractions;
using Axioplan.GammesNomenclatures.Application.Models;
using Axioplan.GammesNomenclatures.Domain.Aps.Capacity;
using Axioplan.GammesNomenclatures.Domain.Aps.Ctp;
using Axioplan.GammesNomenclatures.Infrastructure.Aps;
using Microsoft.Data.SqlClient;

namespace Axioplan.GammesNomenclatures.Infrastructure.Repositories;

public sealed class SqlServerApsCtpRepository(
    ApsSchemaBootstrap schema,
    IApsCapacityRepository capacityRepository) : IApsCtpRepository
{
    public Task EnsureSchemaAsync(CancellationToken cancellationToken = default)
        => schema.EnsureAsync(cancellationToken);

    public async Task SeedDemoAsync(CancellationToken cancellationToken = default)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var connection = schema.OpenConnection();

        await Exec(connection, """
            IF NOT EXISTS (SELECT 1 FROM aps_regime_defaults)
            INSERT INTO aps_regime_defaults
                (article_category, customer_category, article_code, customer_code, period_from, period_to, regime, specificity_rank)
            VALUES
                ('PULL', 'STD', NULL, NULL, NULL, NULL, 'MTO', 1),
                ('PULL', 'STD', 'DEMO_PF', NULL, NULL, NULL, 'MTO', 2),
                (NULL, 'STD', 'DEMO_PF', 'CLI-MTS', NULL, NULL, 'MTS', 4),
                ('PULL', NULL, 'DEMO_PF', 'CLI-MTS', NULL, NULL, 'MTS', 4);
            """, cancellationToken);

        await Exec(connection, """
            IF NOT EXISTS (SELECT 1 FROM aps_ctp_scenarios WHERE code = 'R1')
            INSERT INTO aps_ctp_scenarios
                (code, label, pf_available, yarn_available, yarn_bath, yarn_purchase_required, purchase_lead_days,
                 supplier_mu, supplier_sigma, cap_engageable_day, rho_target, already_loaded, hre_per_unit, hre_status,
                 resource_sigma, use_external, confirmation_status)
            VALUES
                ('R1', 'SIMULE R1 stock PF', 200, 0, NULL, 0, 0, NULL, NULL, 8, 0.85, 0, 0.15, 'OK', 0.05, 0, 'SIMULE'),
                ('R2', 'SIMULE R2 fil + capa', 0, 80, 'BAIN-A', 0, 0, NULL, NULL, 10, 0.85, 1, 0.15, 'OK', NULL, 0, 'SIMULE'),
                ('R2_ST', 'SIMULE R2 sous-traitance', 0, 80, 'BAIN-A', 0, 0, NULL, NULL, 0.5, 0.85, 0.4, 0.15, 'OK', 0.1, 1, 'SIMULE'),
                ('R3', 'SIMULE R3 achat fil', 0, 0, NULL, 1, 21, 21, 5, 10, 0.85, 0, 0.15, 'OK', NULL, 0, 'SIMULE'),
                ('AUTRE_DATE', 'SIMULE autre date', 0, 80, 'BAIN-A', 0, 0, NULL, NULL, 3, 0.85, 2.5, 0.15, 'OK', 0.08, 0, 'SIMULE'),
                ('INFAISABLE', 'SIMULE infaisable', 0, 0, NULL, 1, 60, NULL, NULL, 0.1, 0.85, 0.1, NULL, 'TO_CONFIRM', NULL, 0, 'SIMULE');
            """, cancellationToken);
    }

    public async Task<IReadOnlyList<ApsRegimeDefaultRule>> LoadRegimeDefaultsAsync(CancellationToken cancellationToken = default)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var connection = schema.OpenConnection();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT article_category, customer_category, article_code, customer_code, period_from, period_to, regime, specificity_rank
            FROM aps_regime_defaults
            """;
        var list = new List<ApsRegimeDefaultRule>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new ApsRegimeDefaultRule(
                reader.IsDBNull(0) ? null : reader.GetString(0),
                reader.IsDBNull(1) ? null : reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : DateOnly.FromDateTime(reader.GetDateTime(4)),
                reader.IsDBNull(5) ? null : DateOnly.FromDateTime(reader.GetDateTime(5)),
                reader.GetString(6),
                reader.GetInt32(7)));
        }

        return list;
    }

    public async Task<ApsCtpEvaluationContext> BuildContextAsync(
        ApsCtpDemand demand,
        string effectiveRegime,
        string route,
        string? scenario,
        CancellationToken cancellationToken = default)
    {
        await schema.EnsureAsync(cancellationToken);
        var scen = scenario ?? GuessScenario(demand, effectiveRegime);
        await using var connection = schema.OpenConnection();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT TOP 1 * FROM aps_ctp_scenarios WHERE code = @c";
        cmd.Parameters.AddWithValue("@c", scen);

        double pf = 0, yarn = 0, capDay = 8, rho = 0.85, loaded = 0, lead = 0;
        string? bath = null;
        bool purchase = false;
        double? mu = null, sigma = null, hre = null, resSigma = null;
        string hreStatus = "TO_CONFIRM";
        bool useExternal = false;

        await using (var reader = await cmd.ExecuteReaderAsync(cancellationToken))
        {
            if (await reader.ReadAsync(cancellationToken))
            {
                pf = GetDouble(reader, "pf_available");
                yarn = GetDouble(reader, "yarn_available");
                bath = reader["yarn_bath"] is DBNull ? null : Convert.ToString(reader["yarn_bath"]);
                purchase = Convert.ToBoolean(reader["yarn_purchase_required"]);
                lead = GetDouble(reader, "purchase_lead_days");
                mu = reader["supplier_mu"] is DBNull ? null : Convert.ToDouble(reader["supplier_mu"]);
                sigma = reader["supplier_sigma"] is DBNull ? null : Convert.ToDouble(reader["supplier_sigma"]);
                capDay = GetDouble(reader, "cap_engageable_day");
                rho = GetDouble(reader, "rho_target");
                loaded = GetDouble(reader, "already_loaded");
                hre = reader["hre_per_unit"] is DBNull ? null : Convert.ToDouble(reader["hre_per_unit"]);
                hreStatus = Convert.ToString(reader["hre_status"]) ?? "TO_CONFIRM";
                resSigma = reader["resource_sigma"] is DBNull ? null : Convert.ToDouble(reader["resource_sigma"]);
                useExternal = Convert.ToBoolean(reader["use_external"]);
            }
        }

        // Soustraction réservations matière actives (PF + fil)
        var reservedPf = await SumActiveMaterialAsync(connection, demand.ArticleCode, cancellationToken);
        pf = Math.Max(0, pf - reservedPf);
        var reservedYarn = await SumActiveMaterialAsync(connection, "FIL-DEMO", cancellationToken);
        yarn = Math.Max(0, yarn - reservedYarn);

        var today = DateOnly.FromDateTime(DateTime.Today);
        var days = new List<ApsCtpResourceDay>();
        for (var i = 0; i <= 45; i++)
        {
            var d = today.AddDays(i);
            var reservedCap = await SumActiveCapacityAsync(connection, "CC_REMAILLAGE", d, cancellationToken);
            days.Add(new ApsCtpResourceDay("CC_REMAILLAGE", d, Math.Max(0, capDay - reservedCap), rho, loaded));
        }

        var externals = new List<ApsCtpExternalSlot>();
        if (useExternal || string.Equals(scen, "R2_ST", StringComparison.OrdinalIgnoreCase))
        {
            var engagements = await capacityRepository.GetExternalEngagementsAsync("SEG_B", cancellationToken);
            foreach (var e in engagements)
            {
                var reservedExt = await SumActiveExternalAsync(connection, e.PartnerCode, cancellationToken);
                var residual = Math.Max(0, e.ResidualVolume - reservedExt);
                if (residual <= 0)
                {
                    continue;
                }

                externals.Add(new ApsCtpExternalSlot(
                    e.PartnerCode, e.PartnerCode, residual,
                    e.HandoverDate, e.ReturnDate, e.EngagementDeadline, e.Status,
                    PartnerReliability: 0.9, ReworkRate: 0.05));
            }
        }

        var yarnState = new ApsCtpYarnState(
            CompatibleBathAvailable: yarn >= demand.Quantity && bath is not null,
            BathCode: bath,
            AvailableQty: yarn,
            AvailableFrom: purchase ? today.AddDays((int)Math.Ceiling(lead)) : today,
            PurchaseRequired: purchase,
            SupplierMuDays: mu,
            SupplierSigmaDays: sigma,
            PurchaseLeadDays: lead);

        var load = demand.Quantity * 0.02; // TO_CONFIRM charge unitaire demo remaillage

        return new ApsCtpEvaluationContext(
            demand,
            effectiveRegime,
            route,
            pf,
            yarnState,
            days,
            externals,
            "CIR_REMAIL_INT",
            "CIR_REMAIL_ALT",
            "CC_REMAILLAGE",
            load,
            hre,
            hreStatus,
            resSigma,
            today,
            MaxHorizonDays: 45,
            new ApsFloatThresholds());
    }

    private static string GuessScenario(ApsCtpDemand demand, string regime)
    {
        if (regime.Equals(ApsRegimes.Mts, StringComparison.OrdinalIgnoreCase)
            || demand.CustomerCode.Contains("MTS", StringComparison.OrdinalIgnoreCase))
        {
            return "R1";
        }

        return "R2";
    }

    public async Task<(bool Ok, string? Error, long? PromiseDbId)> CommitPromiseAtomicAsync(
        ApsCtpAnswer answer,
        ApsCtpDemand demand,
        CancellationToken cancellationToken = default)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var connection = schema.OpenConnection();
        await using var tx = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            var promiseId = "PRM-" + demand.DemandId + "-" + DateTime.UtcNow.ToString("HHmmssfff");
            await using (var cmd = connection.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = """
                    INSERT INTO aps_ctp_promises
                        (promise_id, demand_id, status, outcome, proposed_date, effective_regime, bottleneck,
                         reliability, reliability_status, hre, margin_density, float_days, float_status,
                         circuit_code, bath_code, external_engagement, payload_json)
                    OUTPUT INSERTED.id
                    VALUES (@pid, @did, @st, @out, @pd, @reg, @bn, @rel, @rels, @hre, @md, @fd, @fs, @cir, @bath, @ext, @json)
                    """;
                cmd.Parameters.AddWithValue("@pid", promiseId);
                cmd.Parameters.AddWithValue("@did", demand.DemandId);
                cmd.Parameters.AddWithValue("@st", answer.Outcome == ApsCtpOutcomes.Infeasible ? "REFUSED" : "ACTIVE");
                cmd.Parameters.AddWithValue("@out", answer.Outcome);
                cmd.Parameters.AddWithValue("@pd", (object?)answer.ProposedDate?.ToDateTime(TimeOnly.MinValue) ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@reg", answer.EffectiveRegime);
                cmd.Parameters.AddWithValue("@bn", answer.Bottleneck);
                cmd.Parameters.AddWithValue("@rel", (object?)answer.Reliability.Value ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@rels", answer.Reliability.Status);
                cmd.Parameters.AddWithValue("@hre", (object?)answer.Hre ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@md", (object?)answer.MarginDensity ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@fd", (object?)answer.FloatDays ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@fs", (object?)answer.FloatStatus ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@cir", (object?)answer.CircuitCode ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@bath", (object?)answer.SelectedBath ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@ext", (object?)answer.ExternalEngagementCode ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@json", JsonSerializer.Serialize(answer));
                var idObj = await cmd.ExecuteScalarAsync(cancellationToken);
                var dbId = Convert.ToInt64(idObj);

                if (answer.Outcome != ApsCtpOutcomes.Infeasible && answer.ReservesCreated)
                {
                    // Matière
                    await using var mat = connection.CreateCommand();
                    mat.Transaction = tx;
                    mat.CommandText = """
                        INSERT INTO aps_reservations_matiere (promise_id, article_code, bath_code, quantity, status)
                        VALUES (@p, @a, @b, @q, 'ACTIVE')
                        """;
                    mat.Parameters.AddWithValue("@p", promiseId);
                    mat.Parameters.AddWithValue("@a", answer.EffectiveRegime == ApsRegimes.Mts ? demand.ArticleCode : "FIL-DEMO");
                    mat.Parameters.AddWithValue("@b", (object?)answer.SelectedBath ?? DBNull.Value);
                    mat.Parameters.AddWithValue("@q", demand.Quantity);
                    await mat.ExecuteNonQueryAsync(cancellationToken);

                    // Capacité
                    if (answer.ProposedDate is not null && answer.ExternalEngagementCode is null)
                    {
                        await using var cap = connection.CreateCommand();
                        cap.Transaction = tx;
                        cap.CommandText = """
                            INSERT INTO aps_reservations_capacite (promise_id, resource_code, bucket_date, quantity, status)
                            VALUES (@p, @r, @d, @q, 'ACTIVE')
                            """;
                        cap.Parameters.AddWithValue("@p", promiseId);
                        cap.Parameters.AddWithValue("@r", "CC_REMAILLAGE");
                        cap.Parameters.AddWithValue("@d", answer.ProposedDate.Value.ToDateTime(TimeOnly.MinValue));
                        cap.Parameters.AddWithValue("@q", demand.Quantity * 0.02);
                        await cap.ExecuteNonQueryAsync(cancellationToken);

                        // miroir table capacité phase 5
                        await using var cap2 = connection.CreateCommand();
                        cap2.Transaction = tx;
                        cap2.CommandText = """
                            INSERT INTO aps_capacity_reservations (resource_code, bucket_date, quantity, unit, aggregate_id, reason)
                            VALUES ('CC_REMAILLAGE', @d, @q, 'HEURES_PERSONNE', @p, 'ctp-promise')
                            """;
                        cap2.Parameters.AddWithValue("@d", answer.ProposedDate.Value.ToDateTime(TimeOnly.MinValue));
                        cap2.Parameters.AddWithValue("@q", demand.Quantity * 0.02);
                        cap2.Parameters.AddWithValue("@p", promiseId);
                        await cap2.ExecuteNonQueryAsync(cancellationToken);
                    }

                    if (answer.ExternalEngagementCode is not null)
                    {
                        await using var ext = connection.CreateCommand();
                        ext.Transaction = tx;
                        ext.CommandText = """
                            INSERT INTO aps_reservations_externes (promise_id, engagement_code, quantity, status)
                            VALUES (@p, @e, @q, 'ACTIVE')
                            """;
                        ext.Parameters.AddWithValue("@p", promiseId);
                        ext.Parameters.AddWithValue("@e", answer.ExternalEngagementCode);
                        ext.Parameters.AddWithValue("@q", demand.Quantity);
                        await ext.ExecuteNonQueryAsync(cancellationToken);
                    }
                }

                await tx.CommitAsync(cancellationToken);
                return (true, null, dbId);
            }
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(cancellationToken);
            return (false, ex.Message, null);
        }
    }

    public async Task<IReadOnlyList<ApsCtpPromiseDto>> ListPromisesAsync(CancellationToken cancellationToken = default)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var connection = schema.OpenConnection();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT TOP 50 id, promise_id, demand_id, status, outcome, proposed_date, effective_regime, bottleneck,
                   reliability, reliability_status, hre, margin_density, float_days, float_status,
                   circuit_code, bath_code, external_engagement, created_at, payload_json
            FROM aps_ctp_promises ORDER BY id DESC
            """;
        var list = new List<ApsCtpPromiseDto>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new ApsCtpPromiseDto(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.IsDBNull(5) ? null : DateOnly.FromDateTime(reader.GetDateTime(5)),
                reader.GetString(6),
                reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetDouble(8),
                reader.GetString(9),
                reader.IsDBNull(10) ? null : reader.GetDouble(10),
                reader.IsDBNull(11) ? null : reader.GetDouble(11),
                reader.IsDBNull(12) ? null : reader.GetInt32(12),
                reader.IsDBNull(13) ? null : reader.GetString(13),
                reader.IsDBNull(14) ? null : reader.GetString(14),
                reader.IsDBNull(15) ? null : reader.GetString(15),
                reader.IsDBNull(16) ? null : reader.GetString(16),
                reader.GetDateTime(17),
                reader.GetString(18)));
        }

        return list;
    }

    public async Task<(bool Ok, string? Error)> ReleasePromiseDemoAsync(
        long promiseDbId,
        CancellationToken cancellationToken = default)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var connection = schema.OpenConnection();
        await using var tx = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            string? promiseId;
            await using (var cmd = connection.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "SELECT promise_id, status FROM aps_ctp_promises WHERE id = @id";
                cmd.Parameters.AddWithValue("@id", promiseDbId);
                await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
                if (!await reader.ReadAsync(cancellationToken))
                {
                    await tx.RollbackAsync(cancellationToken);
                    return (false, "Promesse introuvable.");
                }

                promiseId = reader.GetString(0);
                var status = reader.GetString(1);
                if (status == "RELEASED")
                {
                    await tx.RollbackAsync(cancellationToken);
                    return (false, "Déjà libérée.");
                }
            }

            foreach (var sql in new[]
                     {
                         "UPDATE aps_reservations_matiere SET status='RELEASED' WHERE promise_id=@p AND status='ACTIVE'",
                         "UPDATE aps_reservations_capacite SET status='RELEASED' WHERE promise_id=@p AND status='ACTIVE'",
                         "UPDATE aps_reservations_externes SET status='RELEASED' WHERE promise_id=@p AND status='ACTIVE'",
                         "UPDATE aps_ctp_promises SET status='RELEASED' WHERE promise_id=@p"
                     })
            {
                await using var u = connection.CreateCommand();
                u.Transaction = tx;
                u.CommandText = sql;
                u.Parameters.AddWithValue("@p", promiseId!);
                await u.ExecuteNonQueryAsync(cancellationToken);
            }

            // Compensation capacité phase 5 (négatif)
            await using var comp = connection.CreateCommand();
            comp.Transaction = tx;
            comp.CommandText = """
                INSERT INTO aps_capacity_reservations (resource_code, bucket_date, quantity, unit, aggregate_id, reason)
                SELECT resource_code, bucket_date, -quantity, 'HEURES_PERSONNE', promise_id, 'ctp-release'
                FROM aps_reservations_capacite WHERE promise_id=@p AND status='RELEASED'
                """;
            comp.Parameters.AddWithValue("@p", promiseId!);
            await comp.ExecuteNonQueryAsync(cancellationToken);

            await tx.CommitAsync(cancellationToken);
            return (true, null);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(cancellationToken);
            return (false, ex.Message);
        }
    }

    public async Task<(bool Valid, double? HrePerUnit, string Status)> LoadHreAsync(
        string articleCode,
        CancellationToken cancellationToken = default)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var connection = schema.OpenConnection();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT TOP 1 hre_per_unit, hre_status FROM aps_ctp_scenarios WHERE code='R2'";
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return (false, null, "TO_CONFIRM");
        }

        double? hre = reader.IsDBNull(0) ? null : reader.GetDouble(0);
        var status = reader.GetString(1);
        return (hre is not null && status == "OK", hre, status);
    }

    private static async Task<double> SumActiveMaterialAsync(SqlConnection connection, string article, CancellationToken ct)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT ISNULL(SUM(quantity),0) FROM aps_reservations_matiere WHERE article_code=@a AND status='ACTIVE'";
        cmd.Parameters.AddWithValue("@a", article);
        return Convert.ToDouble(await cmd.ExecuteScalarAsync(ct) ?? 0);
    }

    private static async Task<double> SumActiveCapacityAsync(SqlConnection connection, string resource, DateOnly day, CancellationToken ct)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT ISNULL(SUM(quantity),0) FROM aps_reservations_capacite
            WHERE resource_code=@r AND bucket_date=@d AND status='ACTIVE'
            """;
        cmd.Parameters.AddWithValue("@r", resource);
        cmd.Parameters.AddWithValue("@d", day.ToDateTime(TimeOnly.MinValue));
        return Convert.ToDouble(await cmd.ExecuteScalarAsync(ct) ?? 0);
    }

    private static async Task<double> SumActiveExternalAsync(SqlConnection connection, string eng, CancellationToken ct)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT ISNULL(SUM(quantity),0) FROM aps_reservations_externes WHERE engagement_code=@e AND status='ACTIVE'";
        cmd.Parameters.AddWithValue("@e", eng);
        return Convert.ToDouble(await cmd.ExecuteScalarAsync(ct) ?? 0);
    }

    private static double GetDouble(SqlDataReader reader, string name)
        => Convert.ToDouble(reader[name]);

    private static async Task Exec(SqlConnection connection, string sql, CancellationToken ct)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
