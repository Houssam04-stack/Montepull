using System.Text.Json;
using Axioplan.GammesNomenclatures.Application.Abstractions;
using Axioplan.GammesNomenclatures.Application.Models;
using Axioplan.GammesNomenclatures.Domain.Mvp0;
using Axioplan.GammesNomenclatures.Infrastructure.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace Axioplan.GammesNomenclatures.Infrastructure.Mvp0;

public sealed class Mvp0SchemaBootstrap(IOptions<DatabaseOptions> options)
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static bool _ready; // schema ensure gate

    public async Task EnsureAsync(CancellationToken cancellationToken = default)
    {
        await Gate.WaitAsync(cancellationToken);
        try
        {
            if (_ready) return;
            await using var connection = OpenConnection();
            foreach (var ddl in Ddl)
            {
                await using var cmd = connection.CreateCommand();
                cmd.CommandText = ddl;
                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }

            _ready = true;
        }
        finally
        {
            Gate.Release();
        }
    }

    public SqlConnection OpenConnection()
    {
        var cs = options.Value.ConnectionString ?? throw new InvalidOperationException("Database:ConnectionString manquant.");
        var c = new SqlConnection(cs);
        c.Open();
        return c;
    }

    private static readonly string[] Ddl =
    [
        """
        IF OBJECT_ID(N'dbo.mvp0_campaigns', N'U') IS NULL
        CREATE TABLE mvp0_campaigns (
            id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
            code NVARCHAR(80) NOT NULL UNIQUE,
            family_code NVARCHAR(80) NOT NULL,
            site_code NVARCHAR(80) NOT NULL,
            period_from DATE NOT NULL,
            period_to DATE NOT NULL,
            owner_name NVARCHAR(120) NOT NULL,
            status NVARCHAR(40) NOT NULL,
            data_source NVARCHAR(80) NOT NULL,
            provenance NVARCHAR(30) NOT NULL,
            created_at DATETIME2 NOT NULL,
            import_version INT NOT NULL,
            go_threshold FLOAT NOT NULL,
            go_res_threshold FLOAT NOT NULL,
            gate_outcome NVARCHAR(40) NULL
        );
        """,
        """
        IF OBJECT_ID(N'dbo.mvp0_import_batches', N'U') IS NULL
        CREATE TABLE mvp0_import_batches (
            id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
            campaign_id UNIQUEIDENTIFIER NOT NULL,
            data_type NVARCHAR(40) NOT NULL,
            file_name NVARCHAR(260) NOT NULL,
            file_hash NVARCHAR(128) NOT NULL,
            version INT NOT NULL,
            lines_read INT NOT NULL,
            lines_imported INT NOT NULL,
            lines_rejected INT NOT NULL,
            status NVARCHAR(30) NOT NULL,
            provenance NVARCHAR(30) NOT NULL,
            imported_at DATETIME2 NOT NULL,
            report_json NVARCHAR(MAX) NOT NULL
        );
        """,
        """
        IF OBJECT_ID(N'dbo.mvp0_working_sets', N'U') IS NULL
        CREATE TABLE mvp0_working_sets (
            campaign_id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
            payload_json NVARCHAR(MAX) NOT NULL,
            updated_at DATETIME2 NOT NULL
        );
        """,
        """
        IF OBJECT_ID(N'dbo.mvp0_anomalies', N'U') IS NULL
        CREATE TABLE mvp0_anomalies (
            id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
            campaign_id UNIQUEIDENTIFIER NOT NULL,
            anomaly_id NVARCHAR(200) NOT NULL,
            payload_json NVARCHAR(MAX) NOT NULL
        );
        """,
        """
        IF OBJECT_ID(N'dbo.mvp0_bypasses', N'U') IS NULL
        CREATE TABLE mvp0_bypasses (
            id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
            campaign_id UNIQUEIDENTIFIER NOT NULL,
            bypass_id NVARCHAR(80) NOT NULL,
            payload_json NVARCHAR(MAX) NOT NULL,
            created_at DATETIME2 NOT NULL
        );
        """,
        """
        IF OBJECT_ID(N'dbo.mvp0_reliability_weights', N'U') IS NULL
        CREATE TABLE mvp0_reliability_weights (
            id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
            articles FLOAT NOT NULL, boms FLOAT NOT NULL, routings FLOAT NOT NULL, times FLOAT NOT NULL,
            calendars FLOAT NOT NULL, trs FLOAT NOT NULL, bom_op FLOAT NOT NULL, wo_hist FLOAT NOT NULL, freshness FLOAT NOT NULL
        );
        """,
        """
        IF OBJECT_ID(N'dbo.mvp0_thresholds', N'U') IS NULL
        CREATE TABLE mvp0_thresholds (
            id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
            go_threshold FLOAT NOT NULL,
            go_res_threshold FLOAT NOT NULL,
            min_cycle FLOAT NOT NULL,
            max_cycle FLOAT NOT NULL,
            aging_days INT NOT NULL,
            stale_days INT NOT NULL,
            date_tol_days INT NOT NULL,
            dur_tol_rel FLOAT NOT NULL,
            note NVARCHAR(200) NOT NULL
        );
        """,
        """
        IF OBJECT_ID(N'dbo.mvp0_scores', N'U') IS NULL
        CREATE TABLE mvp0_scores (
            campaign_id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
            input_json NVARCHAR(MAX) NULL,
            result_json NVARCHAR(MAX) NULL,
            backtest_json NVARCHAR(MAX) NULL,
            gate_json NVARCHAR(MAX) NULL,
            planner_name NVARCHAR(120) NULL,
            planner_comment NVARCHAR(MAX) NULL,
            planner_approved BIT NOT NULL DEFAULT 0,
            report_html NVARCHAR(MAX) NULL
        );
        """,
        """
        IF OBJECT_ID(N'dbo.mvp0_import_files', N'U') IS NULL
        CREATE TABLE mvp0_import_files (
            id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
            batch_id BIGINT NOT NULL,
            campaign_id UNIQUEIDENTIFIER NOT NULL,
            file_name NVARCHAR(260) NOT NULL,
            file_hash NVARCHAR(128) NOT NULL,
            stored_at DATETIME2 NOT NULL
        );
        """,
        """
        IF OBJECT_ID(N'dbo.mvp0_import_mappings', N'U') IS NULL
        CREATE TABLE mvp0_import_mappings (
            id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
            campaign_id UNIQUEIDENTIFIER NOT NULL,
            batch_id BIGINT NOT NULL,
            data_type NVARCHAR(40) NOT NULL,
            mapping_json NVARCHAR(MAX) NOT NULL,
            created_at DATETIME2 NOT NULL
        );
        """,
        """
        IF OBJECT_ID(N'dbo.mvp0_validation_rules', N'U') IS NULL
        CREATE TABLE mvp0_validation_rules (
            rule_code NVARCHAR(80) NOT NULL PRIMARY KEY,
            domain NVARCHAR(40) NOT NULL,
            severity_default NVARCHAR(20) NOT NULL,
            description NVARCHAR(400) NOT NULL,
            configurable BIT NOT NULL DEFAULT 1,
            note NVARCHAR(200) NOT NULL
        );
        """,
        """
        IF OBJECT_ID(N'dbo.mvp0_validation_runs', N'U') IS NULL
        CREATE TABLE mvp0_validation_runs (
            id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
            campaign_id UNIQUEIDENTIFIER NOT NULL,
            run_at DATETIME2 NOT NULL,
            anomaly_count INT NOT NULL,
            blocking_count INT NOT NULL,
            status NVARCHAR(30) NOT NULL
        );
        """,
        """
        IF OBJECT_ID(N'dbo.mvp0_bypass_history', N'U') IS NULL
        CREATE TABLE mvp0_bypass_history (
            id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
            campaign_id UNIQUEIDENTIFIER NOT NULL,
            bypass_id NVARCHAR(80) NOT NULL,
            action NVARCHAR(30) NOT NULL,
            payload_json NVARCHAR(MAX) NOT NULL,
            at_utc DATETIME2 NOT NULL
        );
        """,
        """
        IF OBJECT_ID(N'dbo.mvp0_reports', N'U') IS NULL
        CREATE TABLE mvp0_reports (
            id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
            campaign_id UNIQUEIDENTIFIER NOT NULL,
            version INT NOT NULL,
            html NVARCHAR(MAX) NOT NULL,
            created_at DATETIME2 NOT NULL
        );
        """
    ];
}

public sealed class SqlServerMvp0Repository(Mvp0SchemaBootstrap schema) : IMvp0Repository
{
    private sealed record WorkingSet(
        List<Mvp0ArticleRow> Articles,
        List<Mvp0BomRow> Boms,
        List<Mvp0RoutingOpRow> Ops,
        List<Mvp0CalendarRow> Calendars,
        List<Mvp0WorkOrderActual> Wos,
        List<string> Centers,
        List<string> BomOpLinks);

    public Task EnsureSchemaAsync(CancellationToken cancellationToken = default)
        => schema.EnsureAsync(cancellationToken);

    public async Task SeedDemoAsync(CancellationToken cancellationToken = default)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var c = schema.OpenConnection();
        await Exec(c, """
            IF NOT EXISTS (SELECT 1 FROM mvp0_reliability_weights)
            INSERT INTO mvp0_reliability_weights (articles,boms,routings,times,calendars,trs,bom_op,wo_hist,freshness)
            VALUES (15,15,15,10,8,5,8,14,10);
            """, cancellationToken);
        await Exec(c, """
            IF NOT EXISTS (SELECT 1 FROM mvp0_thresholds)
            INSERT INTO mvp0_thresholds (go_threshold, go_res_threshold, min_cycle, max_cycle, aging_days, stale_days, date_tol_days, dur_tol_rel, note)
            VALUES (85, 70, 0.01, 8, 30, 90, 3, 0.25, 'TO_CONFIRM — seuils demo MVP-0');
            """, cancellationToken);
        await Exec(c, """
            IF NOT EXISTS (SELECT 1 FROM mvp0_validation_rules)
            INSERT INTO mvp0_validation_rules (rule_code, domain, severity_default, description, configurable, note) VALUES
            ('ART_CODE_REQUIRED','Articles','BLOCKING','Code article obligatoire',1,'TO_CONFIRM'),
            ('ART_CODE_UNIQUE','Articles','BLOCKING','Code article unique',1,'TO_CONFIRM'),
            ('BOM_CYCLE','Nomenclatures','BLOCKING','Cycle BOM',0,'TO_CONFIRM'),
            ('RTG_CENTER_UNKNOWN','Gammes','BLOCKING','Centre inconnu',1,'TO_CONFIRM'),
            ('RTG_CYCLE_PLAUSIBILITY','Temps','WARNING','Temps hors plage',1,'TO_CONFIRM'),
            ('BOM_OP_LINK_MISSING','LiaisonBOM-OP','BLOCKING','Liaison BOM-opération absente',1,'TO_CONFIRM');
            """, cancellationToken);
    }

    public async Task SaveImportMappingAsync(Guid campaignId, long batchId, string dataType, string mappingJson, CancellationToken cancellationToken = default)
    {
        await using var c = schema.OpenConnection();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = """
            INSERT INTO mvp0_import_mappings (campaign_id, batch_id, data_type, mapping_json, created_at)
            VALUES (@c,@b,@t,@m,SYSUTCDATETIME());
            INSERT INTO mvp0_import_files (batch_id, campaign_id, file_name, file_hash, stored_at)
            SELECT @b, @c, file_name, file_hash, SYSUTCDATETIME() FROM mvp0_import_batches WHERE id=@b;
            """;
        cmd.Parameters.AddWithValue("@c", campaignId);
        cmd.Parameters.AddWithValue("@b", batchId);
        cmd.Parameters.AddWithValue("@t", dataType);
        cmd.Parameters.AddWithValue("@m", mappingJson);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SaveValidationRulesSnapshotAsync(CancellationToken cancellationToken = default)
    {
        await SeedDemoAsync(cancellationToken);
    }

    public Task<IReadOnlyList<(string SheetName, string Csv)>> ConvertExcelToCsvSheetsAsync(
        byte[] fileContent,
        CancellationToken cancellationToken = default)
        => Task.FromResult(Mvp0ExcelCsvConverter.ToCsvSheets(fileContent));

    public async Task<Mvp0Campaign> CreateCampaignAsync(Mvp0Campaign campaign, CancellationToken cancellationToken = default)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var c = schema.OpenConnection();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = """
            INSERT INTO mvp0_campaigns
                (id, code, family_code, site_code, period_from, period_to, owner_name, status, data_source, provenance,
                 created_at, import_version, go_threshold, go_res_threshold, gate_outcome)
            VALUES (@id,@code,@fam,@site,@from,@to,@own,@st,@src,@prov,@at,@ver,@go,@gres,@gate)
            """;
        AddCampaignParams(cmd, campaign);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
        return campaign;
    }

    public async Task<Mvp0Campaign?> GetCampaignAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var c = schema.OpenConnection();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT * FROM mvp0_campaigns WHERE id=@id";
        cmd.Parameters.AddWithValue("@id", id);
        await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await r.ReadAsync(cancellationToken)) return null;
        return ReadCampaign(r);
    }

    public async Task UpdateCampaignStatusAsync(Guid id, string status, string? gateOutcome = null, CancellationToken cancellationToken = default)
    {
        await using var c = schema.OpenConnection();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = """
            UPDATE mvp0_campaigns SET status=@st,
                gate_outcome=COALESCE(@gate, gate_outcome),
                import_version = CASE WHEN @st='VALIDATING' OR @st='IMPORTING' THEN import_version + CASE WHEN @bump=1 THEN 1 ELSE 0 END ELSE import_version END
            WHERE id=@id
            """;
        // simpler update:
        cmd.CommandText = "UPDATE mvp0_campaigns SET status=@st, gate_outcome=COALESCE(@gate, gate_outcome) WHERE id=@id";
        if (status == Mvp0CampaignStatuses.Validating)
        {
            cmd.CommandText = "UPDATE mvp0_campaigns SET status=@st, import_version=import_version+1, gate_outcome=COALESCE(@gate, gate_outcome) WHERE id=@id";
        }

        cmd.Parameters.AddWithValue("@st", status);
        cmd.Parameters.AddWithValue("@gate", (object?)gateOutcome ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@id", id);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Mvp0CampaignDto>> ListCampaignsAsync(CancellationToken cancellationToken = default)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var c = schema.OpenConnection();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT TOP 50 * FROM mvp0_campaigns ORDER BY created_at DESC";
        var list = new List<Mvp0CampaignDto>();
        await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await r.ReadAsync(cancellationToken))
        {
            var camp = ReadCampaign(r);
            list.Add(new Mvp0CampaignDto(camp.Id, camp.Code, camp.FamilyCode, camp.SiteCode, camp.PeriodFrom, camp.PeriodTo,
                camp.Owner, camp.Status, camp.Provenance, camp.ImportVersion, camp.GateOutcome, camp.CreatedAtUtc));
        }

        return list;
    }

    public async Task<Mvp0ImportBatchDto> SaveImportBatchAsync(Mvp0ImportBatchDto batch, CancellationToken cancellationToken = default)
    {
        await using var c = schema.OpenConnection();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = """
            INSERT INTO mvp0_import_batches
                (campaign_id, data_type, file_name, file_hash, version, lines_read, lines_imported, lines_rejected, status, provenance, imported_at, report_json)
            OUTPUT INSERTED.id
            VALUES (@c,@t,@f,@h,@v,@lr,@li,@lj,@st,@p,@at,@r)
            """;
        cmd.Parameters.AddWithValue("@c", batch.CampaignId);
        cmd.Parameters.AddWithValue("@t", batch.DataType);
        cmd.Parameters.AddWithValue("@f", batch.FileName);
        cmd.Parameters.AddWithValue("@h", batch.FileHash);
        cmd.Parameters.AddWithValue("@v", batch.Version);
        cmd.Parameters.AddWithValue("@lr", batch.LinesRead);
        cmd.Parameters.AddWithValue("@li", batch.LinesImported);
        cmd.Parameters.AddWithValue("@lj", batch.LinesRejected);
        cmd.Parameters.AddWithValue("@st", batch.Status);
        cmd.Parameters.AddWithValue("@p", batch.Provenance);
        cmd.Parameters.AddWithValue("@at", batch.ImportedAtUtc);
        cmd.Parameters.AddWithValue("@r", batch.ReportJson);
        var id = Convert.ToInt64(await cmd.ExecuteScalarAsync(cancellationToken));
        return batch with { Id = id };
    }

    public async Task<IReadOnlyList<Mvp0ImportBatchDto>> ListImportBatchesAsync(Guid campaignId, CancellationToken cancellationToken = default)
    {
        await using var c = schema.OpenConnection();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT id, campaign_id, data_type, file_name, file_hash, version, lines_read, lines_imported, lines_rejected, status, provenance, imported_at, report_json FROM mvp0_import_batches WHERE campaign_id=@c ORDER BY id DESC";
        cmd.Parameters.AddWithValue("@c", campaignId);
        var list = new List<Mvp0ImportBatchDto>();
        await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await r.ReadAsync(cancellationToken))
        {
            list.Add(new Mvp0ImportBatchDto(
                r.GetInt64(0), r.GetGuid(1), r.GetString(2), r.GetString(3), r.GetString(4), r.GetInt32(5),
                r.GetInt32(6), r.GetInt32(7), r.GetInt32(8), r.GetString(9), r.GetString(10), r.GetDateTime(11), r.GetString(12)));
        }

        return list;
    }

    public async Task ReplaceWorkingSetAsync(
        Guid campaignId,
        IReadOnlyList<Mvp0ArticleRow> articles,
        IReadOnlyList<Mvp0BomRow> boms,
        IReadOnlyList<Mvp0RoutingOpRow> ops,
        IReadOnlyList<Mvp0CalendarRow> calendars,
        IReadOnlyList<Mvp0WorkOrderActual> wos,
        IReadOnlySet<string> centers,
        IReadOnlySet<string> bomOpLinks,
        CancellationToken cancellationToken = default)
    {
        var ws = new WorkingSet(articles.ToList(), boms.ToList(), ops.ToList(), calendars.ToList(), wos.ToList(), centers.ToList(), bomOpLinks.ToList());
        await using var c = schema.OpenConnection();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = """
            MERGE mvp0_working_sets AS t
            USING (SELECT @id AS campaign_id) s ON t.campaign_id=s.campaign_id
            WHEN MATCHED THEN UPDATE SET payload_json=@p, updated_at=SYSUTCDATETIME()
            WHEN NOT MATCHED THEN INSERT (campaign_id, payload_json, updated_at) VALUES (@id,@p,SYSUTCDATETIME());
            """;
        cmd.Parameters.AddWithValue("@id", campaignId);
        cmd.Parameters.AddWithValue("@p", JsonSerializer.Serialize(ws));
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<(IReadOnlyList<Mvp0ArticleRow> Articles, IReadOnlyList<Mvp0BomRow> Boms, IReadOnlyList<Mvp0RoutingOpRow> Ops,
        IReadOnlyList<Mvp0CalendarRow> Calendars, IReadOnlyList<Mvp0WorkOrderActual> Wos, IReadOnlySet<string> Centers,
        IReadOnlySet<string> BomOpLinks)> LoadWorkingSetAsync(Guid campaignId, CancellationToken cancellationToken = default)
    {
        await using var c = schema.OpenConnection();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT payload_json FROM mvp0_working_sets WHERE campaign_id=@id";
        cmd.Parameters.AddWithValue("@id", campaignId);
        var json = (string?)await cmd.ExecuteScalarAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(json))
            return ([], [], [], [], [], new HashSet<string>(), new HashSet<string>());
        var ws = JsonSerializer.Deserialize<WorkingSet>(json)!;
        return (ws.Articles, ws.Boms, ws.Ops, ws.Calendars, ws.Wos,
            ws.Centers.ToHashSet(StringComparer.OrdinalIgnoreCase),
            ws.BomOpLinks.ToHashSet(StringComparer.OrdinalIgnoreCase));
    }

    public async Task SaveAnomaliesAsync(Guid campaignId, IReadOnlyList<Mvp0Anomaly> anomalies, CancellationToken cancellationToken = default)
    {
        await using var c = schema.OpenConnection();
        await using (var del = c.CreateCommand())
        {
            del.CommandText = "DELETE FROM mvp0_anomalies WHERE campaign_id=@c";
            del.Parameters.AddWithValue("@c", campaignId);
            await del.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var a in anomalies)
        {
            await using var cmd = c.CreateCommand();
            cmd.CommandText = "INSERT INTO mvp0_anomalies (campaign_id, anomaly_id, payload_json) VALUES (@c,@a,@p)";
            cmd.Parameters.AddWithValue("@c", campaignId);
            cmd.Parameters.AddWithValue("@a", a.AnomalyId);
            cmd.Parameters.AddWithValue("@p", JsonSerializer.Serialize(a));
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var run = c.CreateCommand())
        {
            run.CommandText = """
                INSERT INTO mvp0_validation_runs (campaign_id, run_at, anomaly_count, blocking_count, status)
                VALUES (@c, SYSUTCDATETIME(), @n, @b, 'COMPLETED')
                """;
            run.Parameters.AddWithValue("@c", campaignId);
            run.Parameters.AddWithValue("@n", anomalies.Count);
            run.Parameters.AddWithValue("@b", anomalies.Count(a => a.Severity == Mvp0Severities.Blocking));
            await run.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    public async Task<IReadOnlyList<Mvp0Anomaly>> ListAnomaliesAsync(Guid campaignId, CancellationToken cancellationToken = default)
    {
        await using var c = schema.OpenConnection();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT payload_json FROM mvp0_anomalies WHERE campaign_id=@c";
        cmd.Parameters.AddWithValue("@c", campaignId);
        var list = new List<Mvp0Anomaly>();
        await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await r.ReadAsync(cancellationToken))
            list.Add(JsonSerializer.Deserialize<Mvp0Anomaly>(r.GetString(0))!);
        return list;
    }

    public async Task SaveBypassAsync(Guid campaignId, Mvp0Bypass bypass, CancellationToken cancellationToken = default)
    {
        await using var c = schema.OpenConnection();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = "INSERT INTO mvp0_bypasses (campaign_id, bypass_id, payload_json, created_at) VALUES (@c,@b,@p,@at)";
        cmd.Parameters.AddWithValue("@c", campaignId);
        cmd.Parameters.AddWithValue("@b", bypass.BypassId);
        cmd.Parameters.AddWithValue("@p", JsonSerializer.Serialize(bypass));
        cmd.Parameters.AddWithValue("@at", bypass.CreatedAtUtc);
        await cmd.ExecuteNonQueryAsync(cancellationToken);

        await using var hist = c.CreateCommand();
        hist.CommandText = "INSERT INTO mvp0_bypass_history (campaign_id, bypass_id, action, payload_json, at_utc) VALUES (@c,@b,'CREATED',@p,@at)";
        hist.Parameters.AddWithValue("@c", campaignId);
        hist.Parameters.AddWithValue("@b", bypass.BypassId);
        hist.Parameters.AddWithValue("@p", JsonSerializer.Serialize(bypass));
        hist.Parameters.AddWithValue("@at", bypass.CreatedAtUtc);
        await hist.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Mvp0Bypass>> ListBypassesAsync(Guid campaignId, CancellationToken cancellationToken = default)
    {
        await using var c = schema.OpenConnection();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT payload_json FROM mvp0_bypasses WHERE campaign_id=@c ORDER BY id";
        cmd.Parameters.AddWithValue("@c", campaignId);
        var list = new List<Mvp0Bypass>();
        await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await r.ReadAsync(cancellationToken))
            list.Add(JsonSerializer.Deserialize<Mvp0Bypass>(r.GetString(0))!);
        return list;
    }

    public async Task SaveReliabilityAsync(Guid campaignId, Mvp0InputReliabilityResult input, Mvp0ResultReliabilityResult? result, CancellationToken cancellationToken = default)
    {
        await UpsertScoresAsync(campaignId, inputJson: JsonSerializer.Serialize(input), resultJson: result is null ? null : JsonSerializer.Serialize(result), cancellationToken: cancellationToken);
    }

    public async Task SaveBacktestAsync(Guid campaignId, Mvp0BacktestResult backtest, CancellationToken cancellationToken = default)
        => await UpsertScoresAsync(campaignId, backtestJson: JsonSerializer.Serialize(backtest), cancellationToken: cancellationToken);

    public async Task SaveGateAsync(Guid campaignId, Mvp0GateDecision gate, string plannerName, string comment, bool approved, CancellationToken cancellationToken = default)
        => await UpsertScoresAsync(campaignId, gateJson: JsonSerializer.Serialize(gate), plannerName: plannerName, plannerComment: comment, plannerApproved: approved, cancellationToken: cancellationToken);

    public async Task SaveReportAsync(Guid campaignId, string html, CancellationToken cancellationToken = default)
    {
        await UpsertScoresAsync(campaignId, reportHtml: html, cancellationToken: cancellationToken);
        await using var c = schema.OpenConnection();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = """
            INSERT INTO mvp0_reports (campaign_id, version, html, created_at)
            VALUES (@c, (SELECT ISNULL(MAX(version),0)+1 FROM mvp0_reports WHERE campaign_id=@c), @h, SYSUTCDATETIME())
            """;
        cmd.Parameters.AddWithValue("@c", campaignId);
        cmd.Parameters.AddWithValue("@h", html);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<string?> GetReportAsync(Guid campaignId, CancellationToken cancellationToken = default)
    {
        var row = await LoadScoresRowAsync(campaignId, cancellationToken);
        return row?.ReportHtml;
    }

    public async Task<(string? PlannerName, string? Comment, bool Approved)> GetPlannerReviewAsync(Guid campaignId, CancellationToken cancellationToken = default)
    {
        var row = await LoadScoresRowAsync(campaignId, cancellationToken);
        return (row?.PlannerName, row?.PlannerComment, row?.PlannerApproved ?? false);
    }

    public async Task<(Mvp0InputReliabilityResult? Input, Mvp0ResultReliabilityResult? Result, Mvp0BacktestResult? Backtest, Mvp0GateDecision? Gate)> GetScoresAsync(Guid campaignId, CancellationToken cancellationToken = default)
    {
        var row = await LoadScoresRowAsync(campaignId, cancellationToken);
        if (row is null) return (null, null, null, null);
        return (
            row.InputJson is null ? null : JsonSerializer.Deserialize<Mvp0InputReliabilityResult>(row.InputJson),
            row.ResultJson is null ? null : JsonSerializer.Deserialize<Mvp0ResultReliabilityResult>(row.ResultJson),
            row.BacktestJson is null ? null : JsonSerializer.Deserialize<Mvp0BacktestResult>(row.BacktestJson),
            row.GateJson is null ? null : JsonSerializer.Deserialize<Mvp0GateDecision>(row.GateJson));
    }

    public async Task<Mvp0ReliabilityWeights> GetWeightsAsync(CancellationToken cancellationToken = default)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var c = schema.OpenConnection();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT TOP 1 articles,boms,routings,times,calendars,trs,bom_op,wo_hist,freshness FROM mvp0_reliability_weights";
        await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await r.ReadAsync(cancellationToken))
            return new Mvp0ReliabilityWeights(15, 15, 15, 10, 8, 5, 8, 14, 10, 10, 10, 10, 1);
        return new Mvp0ReliabilityWeights(r.GetDouble(0), r.GetDouble(1), r.GetDouble(2), r.GetDouble(3), r.GetDouble(4), r.GetDouble(5), r.GetDouble(6), r.GetDouble(7), r.GetDouble(8), 10, 10, 10, 1);
    }

    public async Task<(double Go, double GoRes, double MinCycle, double MaxCycle, int AgingDays, int StaleDays, int DateTol, double DurTol)> GetThresholdsAsync(CancellationToken cancellationToken = default)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var c = schema.OpenConnection();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT TOP 1 go_threshold, go_res_threshold, min_cycle, max_cycle, aging_days, stale_days, date_tol_days, dur_tol_rel FROM mvp0_thresholds";
        await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await r.ReadAsync(cancellationToken))
            return (85, 70, 0.01, 8, 30, 90, 3, 0.25);
        return (r.GetDouble(0), r.GetDouble(1), r.GetDouble(2), r.GetDouble(3), r.GetInt32(4), r.GetInt32(5), r.GetInt32(6), r.GetDouble(7));
    }

    private sealed record ScoresRow(string? InputJson, string? ResultJson, string? BacktestJson, string? GateJson, string? PlannerName, string? PlannerComment, bool PlannerApproved, string? ReportHtml);

    private async Task<ScoresRow?> LoadScoresRowAsync(Guid campaignId, CancellationToken ct)
    {
        await using var c = schema.OpenConnection();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT input_json, result_json, backtest_json, gate_json, planner_name, planner_comment, planner_approved, report_html FROM mvp0_scores WHERE campaign_id=@id";
        cmd.Parameters.AddWithValue("@id", campaignId);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return null;
        return new ScoresRow(
            r.IsDBNull(0) ? null : r.GetString(0),
            r.IsDBNull(1) ? null : r.GetString(1),
            r.IsDBNull(2) ? null : r.GetString(2),
            r.IsDBNull(3) ? null : r.GetString(3),
            r.IsDBNull(4) ? null : r.GetString(4),
            r.IsDBNull(5) ? null : r.GetString(5),
            !r.IsDBNull(6) && r.GetBoolean(6),
            r.IsDBNull(7) ? null : r.GetString(7));
    }

    private async Task UpsertScoresAsync(
        Guid campaignId,
        string? inputJson = null,
        string? resultJson = null,
        string? backtestJson = null,
        string? gateJson = null,
        string? plannerName = null,
        string? plannerComment = null,
        bool? plannerApproved = null,
        string? reportHtml = null,
        CancellationToken cancellationToken = default)
    {
        await using var c = schema.OpenConnection();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = """
            MERGE mvp0_scores AS t
            USING (SELECT @id AS campaign_id) s ON t.campaign_id=s.campaign_id
            WHEN MATCHED THEN UPDATE SET
                input_json = COALESCE(@in, t.input_json),
                result_json = COALESCE(@res, t.result_json),
                backtest_json = COALESCE(@bt, t.backtest_json),
                gate_json = COALESCE(@gate, t.gate_json),
                planner_name = COALESCE(@pn, t.planner_name),
                planner_comment = COALESCE(@pc, t.planner_comment),
                planner_approved = COALESCE(@pa, t.planner_approved),
                report_html = COALESCE(@rh, t.report_html)
            WHEN NOT MATCHED THEN INSERT (campaign_id, input_json, result_json, backtest_json, gate_json, planner_name, planner_comment, planner_approved, report_html)
                VALUES (@id,@in,@res,@bt,@gate,@pn,@pc,ISNULL(@pa,0),@rh);
            """;
        cmd.Parameters.AddWithValue("@id", campaignId);
        cmd.Parameters.AddWithValue("@in", (object?)inputJson ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@res", (object?)resultJson ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@bt", (object?)backtestJson ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@gate", (object?)gateJson ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@pn", (object?)plannerName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@pc", (object?)plannerComment ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@pa", (object?)plannerApproved ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@rh", (object?)reportHtml ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddCampaignParams(SqlCommand cmd, Mvp0Campaign campaign)
    {
        cmd.Parameters.AddWithValue("@id", campaign.Id);
        cmd.Parameters.AddWithValue("@code", campaign.Code);
        cmd.Parameters.AddWithValue("@fam", campaign.FamilyCode);
        cmd.Parameters.AddWithValue("@site", campaign.SiteCode);
        cmd.Parameters.AddWithValue("@from", campaign.PeriodFrom.ToDateTime(TimeOnly.MinValue));
        cmd.Parameters.AddWithValue("@to", campaign.PeriodTo.ToDateTime(TimeOnly.MinValue));
        cmd.Parameters.AddWithValue("@own", campaign.Owner);
        cmd.Parameters.AddWithValue("@st", campaign.Status);
        cmd.Parameters.AddWithValue("@src", campaign.DataSource);
        cmd.Parameters.AddWithValue("@prov", campaign.Provenance);
        cmd.Parameters.AddWithValue("@at", campaign.CreatedAtUtc);
        cmd.Parameters.AddWithValue("@ver", campaign.ImportVersion);
        cmd.Parameters.AddWithValue("@go", campaign.GoThreshold);
        cmd.Parameters.AddWithValue("@gres", campaign.GoWithReservationsThreshold);
        cmd.Parameters.AddWithValue("@gate", (object?)campaign.GateOutcome ?? DBNull.Value);
    }

    private static Mvp0Campaign ReadCampaign(SqlDataReader r)
        => new(
            r.GetGuid(r.GetOrdinal("id")),
            r.GetString(r.GetOrdinal("code")),
            r.GetString(r.GetOrdinal("family_code")),
            r.GetString(r.GetOrdinal("site_code")),
            DateOnly.FromDateTime(r.GetDateTime(r.GetOrdinal("period_from"))),
            DateOnly.FromDateTime(r.GetDateTime(r.GetOrdinal("period_to"))),
            r.GetString(r.GetOrdinal("owner_name")),
            r.GetString(r.GetOrdinal("status")),
            r.GetString(r.GetOrdinal("data_source")),
            r.GetString(r.GetOrdinal("provenance")),
            r.GetDateTime(r.GetOrdinal("created_at")),
            r.GetInt32(r.GetOrdinal("import_version")),
            r.GetDouble(r.GetOrdinal("go_threshold")),
            r.GetDouble(r.GetOrdinal("go_res_threshold")),
            r.IsDBNull(r.GetOrdinal("gate_outcome")) ? null : r.GetString(r.GetOrdinal("gate_outcome")));

    private static async Task Exec(SqlConnection c, string sql, CancellationToken ct)
    {
        await using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
