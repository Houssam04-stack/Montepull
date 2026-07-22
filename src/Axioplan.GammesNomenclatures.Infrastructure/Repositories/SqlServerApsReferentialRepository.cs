using System.Text.Json;
using Axioplan.GammesNomenclatures.Application.Abstractions;
using Axioplan.GammesNomenclatures.Application.Models;
using Axioplan.GammesNomenclatures.Domain.Aps.Compiler;
using Axioplan.GammesNomenclatures.Domain.Aps.Referential;
using Axioplan.GammesNomenclatures.Infrastructure.Aps;
using Microsoft.Data.SqlClient;

namespace Axioplan.GammesNomenclatures.Infrastructure.Repositories;

public sealed class SqlServerApsReferentialRepository(ApsSchemaBootstrap schema) : IApsReferentialRepository
{
    public Task EnsureSchemaAsync(CancellationToken cancellationToken = default)
        => schema.EnsureAsync(cancellationToken);

    public async Task SeedDemoMinimalAsync(CancellationToken cancellationToken = default)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var connection = schema.OpenConnection();

        await Exec(connection, """
            IF NOT EXISTS (SELECT 1 FROM aps_calendars WHERE code = 'CAL_SITE')
            INSERT INTO aps_calendars (code, label) VALUES ('CAL_SITE', 'Calendrier site Montepull');
            """, cancellationToken);

        await Exec(connection, """
            IF NOT EXISTS (SELECT 1 FROM aps_work_regimes WHERE code = 'REG_6x24')
            INSERT INTO aps_work_regimes (code, label) VALUES
                ('REG_6x24', 'Tricotage 6j x 24h'),
                ('REG_1x8', 'Aval 1x8');
            """, cancellationToken);

        await Exec(connection, """
            IF NOT EXISTS (SELECT 1 FROM aps_teams t JOIN aps_work_regimes r ON r.id = t.regime_id WHERE r.code = 'REG_6x24')
            BEGIN
                INSERT INTO aps_teams (regime_id, code, start_time, end_time)
                SELECT id, 'MATIN', '06:00', '14:00' FROM aps_work_regimes WHERE code = 'REG_6x24';
                INSERT INTO aps_teams (regime_id, code, start_time, end_time)
                SELECT id, 'APRES_MIDI', '14:00', '22:00' FROM aps_work_regimes WHERE code = 'REG_6x24';
                INSERT INTO aps_teams (regime_id, code, start_time, end_time)
                SELECT id, 'NUIT', '22:00', '06:00' FROM aps_work_regimes WHERE code = 'REG_6x24';
                INSERT INTO aps_teams (regime_id, code, start_time, end_time)
                SELECT id, 'JOUR', '08:00', '17:00' FROM aps_work_regimes WHERE code = 'REG_1x8';
            END
            """, cancellationToken);

        await Exec(connection, """
            IF NOT EXISTS (SELECT 1 FROM aps_places WHERE code = 'MP_ROOT')
            BEGIN
                DECLARE @cal INT = (SELECT id FROM aps_calendars WHERE code = 'CAL_SITE');
                INSERT INTO aps_places (parent_id, level_code, code, label, calendar_id)
                VALUES (NULL, 'SITE', 'MP_ROOT', 'Montepull', @cal);
                DECLARE @site INT = SCOPE_IDENTITY();
                INSERT INTO aps_places (parent_id, level_code, code, label, calendar_id)
                VALUES (@site, 'USINE', 'MP_USINE', 'Usine principale', @cal);
                DECLARE @usine INT = SCOPE_IDENTITY();
                INSERT INTO aps_places (parent_id, level_code, code, label, calendar_id)
                VALUES (@usine, 'ZONE', 'ZONE_TRICOT', 'Zone tricotage', @cal),
                       (@usine, 'ZONE', 'ZONE_REMAIL', 'Zone remaillage', @cal),
                       (@usine, 'ZONE', 'ZONE_AMONT_REMAIL', 'Buffer amont remaillage', @cal);
            END
            """, cancellationToken);

        await Exec(connection, """
            IF NOT EXISTS (SELECT 1 FROM aps_charge_centers WHERE code = 'CC_REMAILLAGE')
            BEGIN
                DECLARE @pTricot INT = (SELECT id FROM aps_places WHERE code = 'ZONE_TRICOT');
                DECLARE @pRemail INT = (SELECT id FROM aps_places WHERE code = 'ZONE_REMAIL');
                INSERT INTO aps_charge_centers (place_id, code, label, resource_type) VALUES
                    (@pTricot, 'CC_TRICOTAGE', 'Tricotage', 'MACHINE'),
                    (@pRemail, 'CC_REMAILLAGE', 'Remaillage', 'MAIN_OEUVRE'),
                    (@pRemail, 'CC_TRAITEMENT', 'Traitement lot', 'LOT');
                INSERT INTO aps_charge_posts (charge_center_id, code, label)
                SELECT id, code + '_P1', label + ' post 1' FROM aps_charge_centers;
                INSERT INTO aps_post_regime_assignments (charge_post_id, regime_id, valid_from)
                SELECT cp.id, wr.id, CAST(GETDATE() AS DATE)
                FROM aps_charge_posts cp
                JOIN aps_charge_centers cc ON cc.id = cp.charge_center_id
                JOIN aps_work_regimes wr ON wr.code = CASE
                    WHEN cc.code LIKE '%TRICOT%' THEN 'REG_6x24' ELSE 'REG_1x8' END;
            END
            """, cancellationToken);

        await Exec(connection, """
            IF NOT EXISTS (SELECT 1 FROM aps_stock_zones)
            BEGIN
                DECLARE @place INT = (SELECT id FROM aps_places WHERE code = 'ZONE_AMONT_REMAIL');
                DECLARE @cc INT = (SELECT id FROM aps_charge_centers WHERE code = 'CC_REMAILLAGE');
                INSERT INTO aps_stock_zones (place_id, role, served_charge_center_id)
                VALUES (@place, 'AMONT', @cc);
            END
            """, cancellationToken);

        await Exec(connection, """
            IF NOT EXISTS (SELECT 1 FROM aps_circuits WHERE code = 'CIR_REMAIL_INT')
            INSERT INTO aps_circuits (code, segment_id, operation_code, circuit_type, post_code, unit_time_minutes, yield_rate)
            VALUES
                ('CIR_REMAIL_INT', 'SEG_B', 'REMAILLAGE', 'INTERNE', 'CC_REMAILLAGE_P1', 12, 0.98),
                ('CIR_REMAIL_EXT', 'SEG_B', 'REMAILLAGE', 'EXTERNE', NULL, 12, 0.95);
            """, cancellationToken);

        await Exec(connection, """
            IF NOT EXISTS (SELECT 1 FROM aps_external_engagements)
            INSERT INTO aps_external_engagements
                (partner_code, segment_id, quantity, handover_date, return_date, engagement_deadline, status, residual_volume)
            VALUES ('ST_REMAIL_01', 'SEG_B', 370, '2026-08-01', '2026-08-15', '2026-06-01', 'OPEN', 370);
            """, cancellationToken);

        // Seed stock lot example if an article exists
        await Exec(connection, """
            IF NOT EXISTS (SELECT 1 FROM aps_stock_lots)
               AND EXISTS (SELECT 1 FROM articles)
            BEGIN
                DECLARE @art INT = (SELECT TOP 1 id FROM articles ORDER BY id);
                DECLARE @zone INT = (SELECT TOP 1 id FROM aps_stock_zones);
                INSERT INTO aps_stock_lots (article_id, lot_code, bath_code, quantity, unit, status, stock_zone_id)
                VALUES (@art, 'LOT-DEMO', 'BAIN-DEMO', 10, 'KG', 'LIBRE', @zone);
            END
            """, cancellationToken);
    }

    public async Task<int> UpsertPlaceAsync(
        string code, string label, string level, int? parentId, CancellationToken cancellationToken = default)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var connection = schema.OpenConnection();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            IF EXISTS (SELECT 1 FROM aps_places WHERE code = @code)
            BEGIN
                UPDATE aps_places SET label = @label, level_code = @level, parent_id = @parent WHERE code = @code;
                SELECT id FROM aps_places WHERE code = @code;
            END
            ELSE
            BEGIN
                INSERT INTO aps_places (parent_id, level_code, code, label)
                OUTPUT INSERTED.id
                VALUES (@parent, @level, @code, @label);
            END
            """;
        cmd.Parameters.AddWithValue("@code", code);
        cmd.Parameters.AddWithValue("@label", label);
        cmd.Parameters.AddWithValue("@level", level);
        cmd.Parameters.AddWithValue("@parent", (object?)parentId ?? DBNull.Value);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken));
    }

    public async Task<IReadOnlyList<ApsPlaceDto>> GetPlacesAsync(CancellationToken cancellationToken = default)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var connection = schema.OpenConnection();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT id, parent_id, level_code, code, label, calendar_id FROM aps_places ORDER BY id";
        var list = new List<ApsPlaceDto>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new ApsPlaceDto(
                reader.GetInt32(0),
                reader.IsDBNull(1) ? null : reader.GetInt32(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetInt32(5)));
        }

        return list;
    }

    public async Task<IReadOnlyList<ApsStockZoneDto>> GetStockZonesAsync(CancellationToken cancellationToken = default)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var connection = schema.OpenConnection();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT z.id, z.place_id, p.code, z.role, z.served_charge_center_id
            FROM aps_stock_zones z JOIN aps_places p ON p.id = z.place_id ORDER BY z.id
            """;
        var list = new List<ApsStockZoneDto>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new ApsStockZoneDto(
                reader.GetInt32(0),
                reader.GetInt32(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetInt32(4)));
        }

        return list;
    }

    public async Task<IReadOnlyList<ApsChargeCenterDto>> GetChargeCentersAsync(CancellationToken cancellationToken = default)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var connection = schema.OpenConnection();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT id, place_id, code, label, resource_type FROM aps_charge_centers ORDER BY code";
        var list = new List<ApsChargeCenterDto>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new ApsChargeCenterDto(
                reader.GetInt32(0), reader.GetInt32(1), reader.GetString(2), reader.GetString(3), reader.GetString(4)));
        }

        return list;
    }

    public async Task<IReadOnlyList<ApsChargePostDto>> GetChargePostsAsync(CancellationToken cancellationToken = default)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var connection = schema.OpenConnection();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT id, charge_center_id, code, label FROM aps_charge_posts ORDER BY code";
        var list = new List<ApsChargePostDto>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new ApsChargePostDto(reader.GetInt32(0), reader.GetInt32(1), reader.GetString(2), reader.GetString(3)));
        }

        return list;
    }

    public async Task<IReadOnlyList<ApsCalendarDto>> GetCalendarsAsync(CancellationToken cancellationToken = default)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var connection = schema.OpenConnection();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT id, code, label FROM aps_calendars ORDER BY code";
        var list = new List<ApsCalendarDto>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new ApsCalendarDto(reader.GetInt32(0), reader.GetString(1), reader.GetString(2)));
        }

        return list;
    }

    public async Task<IReadOnlyList<ApsWorkRegimeDto>> GetWorkRegimesAsync(CancellationToken cancellationToken = default)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var connection = schema.OpenConnection();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT id, code, label FROM aps_work_regimes ORDER BY code";
        var list = new List<ApsWorkRegimeDto>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new ApsWorkRegimeDto(reader.GetInt32(0), reader.GetString(1), reader.GetString(2)));
        }

        return list;
    }

    public async Task<IReadOnlyList<ApsTeamDto>> GetTeamsAsync(CancellationToken cancellationToken = default)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var connection = schema.OpenConnection();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT id, regime_id, code, start_time, end_time FROM aps_teams ORDER BY regime_id, code";
        var list = new List<ApsTeamDto>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new ApsTeamDto(
                reader.GetInt32(0),
                reader.GetInt32(1),
                reader.GetString(2),
                reader.GetTimeSpan(3),
                reader.GetTimeSpan(4)));
        }

        return list;
    }

    public async Task<ApsResourceScheduleDto> GetResourceScheduleAsync(
        string resourceCode,
        CancellationToken cancellationToken = default)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var connection = schema.OpenConnection();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT TOP 1 cal.code, wr.code, t.code,
                   CASE
                       WHEN t.start_time <= t.end_time THEN DATEDIFF(minute, t.start_time, t.end_time) / 60.0
                       ELSE (1440 - DATEDIFF(minute, t.end_time, t.start_time)) / 60.0
                   END AS duration_hours
            FROM aps_charge_centers cc
            JOIN aps_places p ON p.id = cc.place_id
            LEFT JOIN aps_calendars cal ON cal.id = p.calendar_id
            LEFT JOIN aps_charge_posts cp ON cp.charge_center_id = cc.id
            LEFT JOIN aps_post_regime_assignments pra ON pra.charge_post_id = cp.id
                AND pra.valid_from <= CAST(GETDATE() AS DATE)
                AND (pra.valid_to IS NULL OR pra.valid_to >= CAST(GETDATE() AS DATE))
            LEFT JOIN aps_work_regimes wr ON wr.id = pra.regime_id
            LEFT JOIN aps_teams t ON t.regime_id = wr.id
            WHERE cc.code = @code
            ORDER BY CASE WHEN t.code = 'JOUR' THEN 0 WHEN t.code = 'MATIN' THEN 1 ELSE 2 END
            """;
        cmd.Parameters.AddWithValue("@code", resourceCode);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            return new ApsResourceScheduleDto(
                resourceCode,
                reader.IsDBNull(0) ? "CAL_SITE" : reader.GetString(0),
                reader.IsDBNull(1) ? FallbackRegime(resourceCode) : reader.GetString(1),
                reader.IsDBNull(2) ? FallbackTeam(resourceCode) : reader.GetString(2),
                reader.IsDBNull(3) ? FallbackDuration(resourceCode) : reader.GetDouble(3));
        }

        return new ApsResourceScheduleDto(
            resourceCode,
            "CAL_SITE",
            FallbackRegime(resourceCode),
            FallbackTeam(resourceCode),
            FallbackDuration(resourceCode));
    }

    private static string FallbackRegime(string resourceCode)
        => resourceCode.Contains("TRICOT", StringComparison.OrdinalIgnoreCase) ? "REG_6x24" : "REG_1x8";

    private static string FallbackTeam(string resourceCode)
        => resourceCode.Contains("TRICOT", StringComparison.OrdinalIgnoreCase) ? "MATIN" : "JOUR";

    private static double FallbackDuration(string resourceCode)
        => resourceCode.Contains("TRICOT", StringComparison.OrdinalIgnoreCase) ? 24 : 8;

    public async Task<IReadOnlyList<ApsStockLotDto>> GetStockLotsAsync(CancellationToken cancellationToken = default)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var connection = schema.OpenConnection();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT s.id, s.article_id, a.code, s.lot_code, s.bath_code, s.quantity, s.unit, s.status, s.stock_zone_id
            FROM aps_stock_lots s JOIN articles a ON a.id = s.article_id
            ORDER BY s.id
            """;
        var list = new List<ApsStockLotDto>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new ApsStockLotDto(
                reader.GetInt32(0),
                reader.GetInt32(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.GetDouble(5),
                reader.GetString(6),
                reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetInt32(8)));
        }

        return list;
    }

    public async Task<IReadOnlyList<ApsCircuitDto>> GetCircuitsAsync(CancellationToken cancellationToken = default)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var connection = schema.OpenConnection();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, code, segment_id, operation_code, circuit_type, post_code, supplier_code,
                   unit_time_minutes, yield_rate, activation_lead_days, traversal_lead_days
            FROM aps_circuits ORDER BY code
            """;
        var list = new List<ApsCircuitDto>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new ApsCircuitDto(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.GetDouble(7),
                reader.GetDouble(8),
                reader.GetDouble(9),
                reader.GetDouble(10)));
        }

        return list;
    }

    public async Task<IReadOnlyList<ApsExternalEngagementDto>> GetExternalEngagementsAsync(CancellationToken cancellationToken = default)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var connection = schema.OpenConnection();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, partner_code, segment_id, quantity, handover_date, return_date,
                   engagement_deadline, status, residual_volume
            FROM aps_external_engagements ORDER BY id
            """;
        var list = new List<ApsExternalEngagementDto>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new ApsExternalEngagementDto(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetDouble(3),
                DateOnly.FromDateTime(reader.GetDateTime(4)),
                DateOnly.FromDateTime(reader.GetDateTime(5)),
                DateOnly.FromDateTime(reader.GetDateTime(6)),
                reader.GetString(7),
                reader.GetDouble(8)));
        }

        return list;
    }

    public async Task<IReadOnlyList<ApsSupplierLeadTimeDto>> GetSupplierLeadTimesAsync(CancellationToken cancellationToken = default)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var connection = schema.OpenConnection();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, article_code, supplier_code, standard_lead_days, observed_mu_days,
                   observed_sigma_days, moq, multiple_lot
            FROM aps_supplier_lead_times ORDER BY article_code
            """;
        var list = new List<ApsSupplierLeadTimeDto>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new ApsSupplierLeadTimeDto(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetDouble(3),
                reader.GetDouble(4),
                reader.GetDouble(5),
                reader.IsDBNull(6) ? null : reader.GetDouble(6),
                reader.IsDBNull(7) ? null : reader.GetDouble(7)));
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

public sealed class SqlServerApsCompilerRepository(ApsSchemaBootstrap schema) : IApsCompilerRepository
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = false };

    public Task EnsureSchemaAsync(CancellationToken cancellationToken = default)
        => schema.EnsureAsync(cancellationToken);

    public async Task PersistBundleAsync(ApsCompileBundle bundle, CancellationToken cancellationToken = default)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var connection = schema.OpenConnection();

        // Invalide les anciens VALID pour la meme cle (rebuild propre).
        await using (var inv = connection.CreateCommand())
        {
            inv.CommandText = """
                UPDATE aps_compiled_artifacts
                SET status = @invalid
                WHERE root_article_code = @root AND segment = @seg AND circuit_chain = @chain AND status = @valid
                """;
            inv.Parameters.AddWithValue("@invalid", ApsCompilerStatuses.Invalid);
            inv.Parameters.AddWithValue("@valid", ApsCompilerStatuses.Valid);
            inv.Parameters.AddWithValue("@root", bundle.Key.RootArticleCode);
            inv.Parameters.AddWithValue("@seg", bundle.Key.Segment);
            inv.Parameters.AddWithValue("@chain", bundle.Key.CircuitChain);
            await inv.ExecuteNonQueryAsync(cancellationToken);
        }

        await InsertArtifact(connection, bundle, ApsCompilerArtifactKinds.Needs, JsonSerializer.Serialize(bundle.Needs, JsonOpts), cancellationToken);
        await InsertArtifact(connection, bundle, ApsCompilerArtifactKinds.Loads, JsonSerializer.Serialize(bundle.Loads, JsonOpts), cancellationToken);
        await InsertArtifact(connection, bundle, ApsCompilerArtifactKinds.Hre, JsonSerializer.Serialize(bundle.Hre, JsonOpts), cancellationToken);
        await InsertArtifact(connection, bundle, ApsCompilerArtifactKinds.LeadTime, JsonSerializer.Serialize(bundle.LeadTimes, JsonOpts), cancellationToken);
    }

    private static async Task InsertArtifact(
        SqlConnection connection,
        ApsCompileBundle bundle,
        string kind,
        string payload,
        CancellationToken cancellationToken)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO aps_compiled_artifacts
                (root_article_code, segment, circuit_chain, artifact_kind, payload_json, source_hash, status, compiled_at, notes)
            VALUES (@root, @seg, @chain, @kind, @payload, @hash, @status, SYSUTCDATETIME(), @notes)
            """;
        cmd.Parameters.AddWithValue("@root", bundle.Key.RootArticleCode);
        cmd.Parameters.AddWithValue("@seg", bundle.Key.Segment);
        cmd.Parameters.AddWithValue("@chain", bundle.Key.CircuitChain);
        cmd.Parameters.AddWithValue("@kind", kind);
        cmd.Parameters.AddWithValue("@payload", payload);
        cmd.Parameters.AddWithValue("@hash", bundle.SourceHash);
        cmd.Parameters.AddWithValue("@status", ApsCompilerStatuses.Valid);
        cmd.Parameters.AddWithValue("@notes", bundle.Notes);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ApsCompiledArtifactDto>> ListArtifactsAsync(
        string? rootArticleCode = null,
        string? status = null,
        CancellationToken cancellationToken = default)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var connection = schema.OpenConnection();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT TOP 500 id, root_article_code, segment, circuit_chain, artifact_kind,
                   payload_json, source_hash, status, compiled_at, notes
            FROM aps_compiled_artifacts
            WHERE (@root IS NULL OR root_article_code = @root)
              AND (@status IS NULL OR status = @status)
            ORDER BY compiled_at DESC, id DESC
            """;
        cmd.Parameters.AddWithValue("@root", (object?)rootArticleCode ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@status", (object?)status ?? DBNull.Value);
        var list = new List<ApsCompiledArtifactDto>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new ApsCompiledArtifactDto(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.GetString(7),
                reader.GetDateTime(8),
                reader.IsDBNull(9) ? string.Empty : reader.GetString(9)));
        }

        return list;
    }

    public async Task InvalidateBySourceMarkerAsync(string sourceMarker, CancellationToken cancellationToken = default)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var connection = schema.OpenConnection();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            UPDATE a SET status = @invalid
            FROM aps_compiled_artifacts a
            INNER JOIN aps_compile_source_index i
                ON i.root_article_code = a.root_article_code
               AND i.segment = a.segment
               AND i.circuit_chain = a.circuit_chain
            WHERE i.source_marker = @marker AND a.status = @valid
            """;
        cmd.Parameters.AddWithValue("@invalid", ApsCompilerStatuses.Invalid);
        cmd.Parameters.AddWithValue("@valid", ApsCompilerStatuses.Valid);
        cmd.Parameters.AddWithValue("@marker", sourceMarker);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task InvalidateArtifactAsync(long artifactId, CancellationToken cancellationToken = default)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var connection = schema.OpenConnection();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "UPDATE aps_compiled_artifacts SET status = @invalid WHERE id = @id";
        cmd.Parameters.AddWithValue("@invalid", ApsCompilerStatuses.Invalid);
        cmd.Parameters.AddWithValue("@id", artifactId);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task RegisterSourceDependencyAsync(
        string sourceMarker,
        string rootArticleCode,
        string segment,
        string circuitChain,
        CancellationToken cancellationToken = default)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var connection = schema.OpenConnection();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            IF NOT EXISTS (
                SELECT 1 FROM aps_compile_source_index
                WHERE source_marker = @m AND root_article_code = @r AND segment = @s AND circuit_chain = @c)
            INSERT INTO aps_compile_source_index (source_marker, root_article_code, segment, circuit_chain)
            VALUES (@m, @r, @s, @c)
            """;
        cmd.Parameters.AddWithValue("@m", sourceMarker);
        cmd.Parameters.AddWithValue("@r", rootArticleCode);
        cmd.Parameters.AddWithValue("@s", segment);
        cmd.Parameters.AddWithValue("@c", circuitChain);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<string>> GetDependentCompileKeysAsync(
        string sourceMarker,
        CancellationToken cancellationToken = default)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var connection = schema.OpenConnection();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT DISTINCT root_article_code + '|' + segment + '|' + circuit_chain
            FROM aps_compile_source_index WHERE source_marker = @m
            """;
        cmd.Parameters.AddWithValue("@m", sourceMarker);
        var list = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(reader.GetString(0));
        }

        return list;
    }
}
