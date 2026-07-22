using Axioplan.GammesNomenclatures.Application.Abstractions;
using Axioplan.GammesNomenclatures.Application.Models;
using Axioplan.GammesNomenclatures.Domain.Aps.Capacity;
using Axioplan.GammesNomenclatures.Infrastructure.Aps;
using Microsoft.Data.SqlClient;

namespace Axioplan.GammesNomenclatures.Infrastructure.Repositories;

public sealed class SqlServerApsCapacityRepository(ApsSchemaBootstrap schema) : IApsCapacityRepository
{
    public Task EnsureSchemaAsync(CancellationToken cancellationToken = default)
        => schema.EnsureAsync(cancellationToken);

    public async Task SeedDemoAsync(CancellationToken cancellationToken = default)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var connection = schema.OpenConnection();

        await Exec(connection, """
            IF NOT EXISTS (SELECT 1 FROM aps_capacity_resources WHERE code = 'CC_REMAILLAGE')
            INSERT INTO aps_capacity_resources
                (code, label, resource_type, unit, resource_count, default_duration_hours, rho_target, confirmation_status, pcs_per_day_ref)
            VALUES
                ('CC_REMAILLAGE', 'Remaillage (goulot)', 'MAIN_OEUVRE', 'HEURES_PERSONNE', 1, 8, 0.85, 'TO_CONFIRM', 2500),
                ('CC_TRICOTAGE', 'Tricotage', 'MACHINE', 'HEURES_MACHINE', 1, 24, 0.82, 'TO_CONFIRM', 2870),
                ('CC_TRAITEMENT', 'Traitement LOT', 'LOT', 'BAINS', 96, 0, 0.80, 'OK', 3360);
            UPDATE aps_capacity_resources SET bath_max_fill = 40, bath_observed_fill = 35
            WHERE code = 'CC_TRAITEMENT' AND bath_max_fill IS NULL;
            """, cancellationToken);

        await Exec(connection, """
            IF NOT EXISTS (SELECT 1 FROM aps_capacity_eta WHERE resource_code = 'CC_REMAILLAGE')
            INSERT INTO aps_capacity_eta (resource_code, family_code, team_code, eta) VALUES
                ('CC_REMAILLAGE', NULL, 'JOUR', 0.90),
                ('CC_REMAILLAGE', 'PULL', 'JOUR', 0.88),
                ('CC_TRICOTAGE', NULL, 'MATIN', 0.92),
                ('CC_TRICOTAGE', NULL, 'NUIT', 0.85),
                ('CC_TRAITEMENT', NULL, NULL, 1.00);
            """, cancellationToken);

        await Exec(connection, """
            IF NOT EXISTS (SELECT 1 FROM aps_capacity_unavailabilities WHERE resource_code = 'CC_REMAILLAGE')
            INSERT INTO aps_capacity_unavailabilities (resource_code, from_date, to_date, quantity, reason)
            VALUES ('CC_REMAILLAGE', CAST(GETDATE() AS DATE), CAST(GETDATE() AS DATE), 1, 'Conges SIMULES');
            """, cancellationToken);

        await Exec(connection, """
            IF NOT EXISTS (SELECT 1 FROM aps_elasticity_tiers WHERE code = 'HS_REMAIL')
            INSERT INTO aps_elasticity_tiers
                (code, resource_code, activation_lead_days, gain, tier_eta, cost_future, unit, confirmation_status)
            VALUES
                ('HS_REMAIL', 'CC_REMAILLAGE', 0, 1.6, 0.80, NULL, 'HEURES_PERSONNE', 'TO_CONFIRM'),
                ('DIM_TRICOT', 'CC_TRICOTAGE', 0, 3.84, 1.00, NULL, 'HEURES_MACHINE', 'TO_CONFIRM'),
                ('ST_REMAIL', 'CC_REMAILLAGE', 45, 370, 1.00, 0, 'PCS_JOUR', 'TO_CONFIRM'),
                ('RECRUTE_REMAIL', 'CC_REMAILLAGE', 60, 8, 0.75, NULL, 'HEURES_PERSONNE', 'TO_CONFIRM');
            """, cancellationToken);

        await Exec(connection, """
            IF NOT EXISTS (SELECT 1 FROM aps_external_engagements WHERE partner_code = 'ST_REMAIL_01')
            INSERT INTO aps_external_engagements
                (partner_code, segment_id, quantity, handover_date, return_date, engagement_deadline, status, residual_volume)
            VALUES ('ST_REMAIL_01', 'SEG_B', 370, DATEADD(DAY, 20, CAST(GETDATE() AS DATE)),
                    DATEADD(DAY, 40, CAST(GETDATE() AS DATE)), DATEADD(DAY, 10, CAST(GETDATE() AS DATE)), 'OPEN', 370);
            """, cancellationToken);
    }

    public async Task<IReadOnlyList<ApsCapacityResourceDto>> ListResourcesAsync(CancellationToken cancellationToken = default)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var connection = schema.OpenConnection();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT code, label, resource_type, unit, resource_count, default_duration_hours, rho_target, confirmation_status
            FROM aps_capacity_resources ORDER BY code
            """;
        var list = new List<ApsCapacityResourceDto>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new ApsCapacityResourceDto(
                reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                reader.GetDouble(4), reader.GetDouble(5), reader.GetDouble(6), reader.GetString(7)));
        }

        return list;
    }

    public async Task<ApsCapacityResourceDto?> GetResourceAsync(string code, CancellationToken cancellationToken = default)
    {
        var all = await ListResourcesAsync(cancellationToken);
        return all.FirstOrDefault(r => r.Code.Equals(code, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<IReadOnlyList<ApsUnavailability>> GetUnavailabilitiesAsync(
        string resourceCode, DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var connection = schema.OpenConnection();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT from_date, to_date, quantity, reason
            FROM aps_capacity_unavailabilities
            WHERE resource_code = @r AND to_date >= @from AND from_date <= @to
            """;
        cmd.Parameters.AddWithValue("@r", resourceCode);
        cmd.Parameters.AddWithValue("@from", from.ToDateTime(TimeOnly.MinValue));
        cmd.Parameters.AddWithValue("@to", to.ToDateTime(TimeOnly.MinValue));
        var list = new List<ApsUnavailability>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new ApsUnavailability(
                DateOnly.FromDateTime(reader.GetDateTime(0)),
                DateOnly.FromDateTime(reader.GetDateTime(1)),
                reader.GetDouble(2),
                reader.GetString(3)));
        }

        return list;
    }

    public async Task<double> GetEtaAsync(
        string resourceCode, string? familyCode, string? teamCode, CancellationToken cancellationToken = default)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var connection = schema.OpenConnection();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT TOP 1 eta FROM aps_capacity_eta
            WHERE resource_code = @r
              AND (family_code = @f OR family_code IS NULL)
              AND (team_code = @t OR team_code IS NULL OR @t IS NULL)
            ORDER BY CASE WHEN family_code = @f THEN 0 ELSE 1 END,
                     CASE WHEN team_code = @t THEN 0 ELSE 1 END
            """;
        cmd.Parameters.AddWithValue("@r", resourceCode);
        cmd.Parameters.AddWithValue("@f", (object?)familyCode ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@t", (object?)teamCode ?? DBNull.Value);
        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return result is double d ? d : result is float f ? f : 1.0;
    }

    public async Task<IReadOnlyList<ApsCapacityReservation>> GetReservationsAsync(
        string resourceCode, DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var connection = schema.OpenConnection();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT resource_code, bucket_date, quantity, unit, aggregate_id, reason
            FROM aps_capacity_reservations
            WHERE resource_code = @r AND bucket_date BETWEEN @from AND @to
            """;
        cmd.Parameters.AddWithValue("@r", resourceCode);
        cmd.Parameters.AddWithValue("@from", from.ToDateTime(TimeOnly.MinValue));
        cmd.Parameters.AddWithValue("@to", to.ToDateTime(TimeOnly.MinValue));
        var list = new List<ApsCapacityReservation>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new ApsCapacityReservation(
                reader.GetString(0),
                DateOnly.FromDateTime(reader.GetDateTime(1)),
                reader.GetDouble(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5)));
        }

        return list;
    }

    public async Task<IReadOnlyList<ApsElasticityTier>> GetTiersAsync(string resourceCode, CancellationToken cancellationToken = default)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var connection = schema.OpenConnection();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT code, resource_code, activation_lead_days, gain, tier_eta, cost_future, unit, confirmation_status
            FROM aps_elasticity_tiers WHERE resource_code = @r
            """;
        cmd.Parameters.AddWithValue("@r", resourceCode);
        var list = new List<ApsElasticityTier>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new ApsElasticityTier(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetDouble(2),
                reader.GetDouble(3),
                reader.GetDouble(4),
                reader.IsDBNull(5) ? null : reader.GetDouble(5),
                reader.GetString(6),
                reader.GetString(7)));
        }

        return list;
    }

    public async Task<IReadOnlyList<ApsExternalEngagementCapacity>> GetExternalEngagementsAsync(
        string segmentId, CancellationToken cancellationToken = default)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var connection = schema.OpenConnection();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT partner_code, segment_id, residual_volume, handover_date, return_date, engagement_deadline, status
            FROM aps_external_engagements WHERE segment_id = @s
            """;
        cmd.Parameters.AddWithValue("@s", segmentId);
        var list = new List<ApsExternalEngagementCapacity>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new ApsExternalEngagementCapacity(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetDouble(2),
                DateOnly.FromDateTime(reader.GetDateTime(3)),
                DateOnly.FromDateTime(reader.GetDateTime(4)),
                DateOnly.FromDateTime(reader.GetDateTime(5)),
                reader.GetString(6)));
        }

        return list;
    }

    public async Task ReserveAsync(ApsCapacityReservation reservation, CancellationToken cancellationToken = default)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var connection = schema.OpenConnection();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO aps_capacity_reservations (resource_code, bucket_date, quantity, unit, aggregate_id, reason)
            VALUES (@r, @d, @q, @u, @a, @reason)
            """;
        cmd.Parameters.AddWithValue("@r", reservation.ResourceCode);
        cmd.Parameters.AddWithValue("@d", reservation.BucketDate.ToDateTime(TimeOnly.MinValue));
        cmd.Parameters.AddWithValue("@q", reservation.Quantity);
        cmd.Parameters.AddWithValue("@u", reservation.Unit);
        cmd.Parameters.AddWithValue("@a", reservation.AggregateId);
        cmd.Parameters.AddWithValue("@reason", (object?)reservation.Reason ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task ReleaseAsync(
        string resourceCode, DateOnly bucketDate, string aggregateId, double quantity, CancellationToken cancellationToken = default)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var connection = schema.OpenConnection();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO aps_capacity_reservations (resource_code, bucket_date, quantity, unit, aggregate_id, reason)
            SELECT @r, @d, -@q, unit, @a, 'liberation'
            FROM aps_capacity_resources WHERE code = @r
            """;
        cmd.Parameters.AddWithValue("@r", resourceCode);
        cmd.Parameters.AddWithValue("@d", bucketDate.ToDateTime(TimeOnly.MinValue));
        cmd.Parameters.AddWithValue("@q", quantity);
        cmd.Parameters.AddWithValue("@a", aggregateId);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<double?> GetBathMaxFillAsync(string resourceCode, CancellationToken cancellationToken = default)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var connection = schema.OpenConnection();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT bath_max_fill FROM aps_capacity_resources WHERE code = @r";
        cmd.Parameters.AddWithValue("@r", resourceCode);
        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        if (result is null or DBNull)
        {
            return null;
        }

        return Convert.ToDouble(result);
    }

    private static async Task Exec(SqlConnection connection, string sql, CancellationToken ct)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
