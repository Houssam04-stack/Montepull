using Axioplan.GammesNomenclatures.Infrastructure.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace Axioplan.GammesNomenclatures.Infrastructure.Aps;

/// <summary>
/// Cree le schema APS de facon additive (meme ideologie que sim_* EnsureSchema).
/// </summary>
public sealed class ApsSchemaBootstrap(IOptions<DatabaseOptions> options)
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static bool _ready;

    public async Task EnsureAsync(CancellationToken cancellationToken = default)
    {
        await Gate.WaitAsync(cancellationToken);
        try
        {
            await using var connection = OpenConnection();
            if (!_ready)
            {
                foreach (var ddl in DdlBatches)
                {
                    await using var cmd = connection.CreateCommand();
                    cmd.CommandText = ddl;
                    await cmd.ExecuteNonQueryAsync(cancellationToken);
                }

                _ready = true;
            }

            // Batches additifs rejouables (IF NOT EXISTS) — phase 7 flux/buffer.
            foreach (var ddl in FluxDdlBatches)
            {
                await using var cmd = connection.CreateCommand();
                cmd.CommandText = ddl;
                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }

            foreach (var ddl in CtpDdlBatches)
            {
                await using var cmd = connection.CreateCommand();
                cmd.CommandText = ddl;
                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }

            foreach (var ddl in Phase9DdlBatches)
            {
                await using var cmd = connection.CreateCommand();
                cmd.CommandText = ddl;
                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }
        }
        finally
        {
            Gate.Release();
        }
    }

    public SqlConnection OpenConnection()
    {
        var cs = options.Value.ConnectionString
                 ?? throw new InvalidOperationException("Database:ConnectionString manquant.");
        var connection = new SqlConnection(cs);
        connection.Open();
        return connection;
    }

    private static readonly string[] DdlBatches =
    [
        """
        IF OBJECT_ID(N'dbo.aps_journal_events', N'U') IS NULL
        BEGIN
            CREATE TABLE aps_journal_events (
                event_id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                event_type NVARCHAR(80) NOT NULL,
                occurred_at DATETIME2 NOT NULL,
                recorded_at DATETIME2 NOT NULL CONSTRAINT DF_aps_journal_recorded DEFAULT SYSUTCDATETIME(),
                aggregate_id NVARCHAR(200) NOT NULL,
                payload_json NVARCHAR(MAX) NOT NULL,
                actor NVARCHAR(120) NOT NULL,
                schema_version NVARCHAR(20) NOT NULL,
                causation_id NVARCHAR(200) NULL,
                correlation_id NVARCHAR(200) NULL
            );
            CREATE INDEX IX_aps_journal_type_occurred ON aps_journal_events(event_type, occurred_at);
            CREATE INDEX IX_aps_journal_aggregate ON aps_journal_events(aggregate_id, occurred_at);
        END
        """,
        """
        IF OBJECT_ID(N'dbo.aps_expectations', N'U') IS NULL
        BEGIN
            CREATE TABLE aps_expectations (
                id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                expected_id NVARCHAR(200) NOT NULL,
                expected_type NVARCHAR(80) NOT NULL,
                emitted_by NVARCHAR(20) NOT NULL,
                grain NVARCHAR(30) NOT NULL,
                plan_ref NVARCHAR(200) NULL,
                earliest_at DATETIME2 NOT NULL,
                latest_at DATETIME2 NOT NULL,
                hre FLOAT NULL,
                causation_id NVARCHAR(200) NULL,
                aggregate_id NVARCHAR(200) NOT NULL,
                payload_json NVARCHAR(MAX) NOT NULL,
                emitted_at DATETIME2 NOT NULL CONSTRAINT DF_aps_exp_emitted DEFAULT SYSUTCDATETIME(),
                schema_version NVARCHAR(20) NOT NULL,
                CONSTRAINT uq_aps_expectations_expected_id UNIQUE (expected_id)
            );
            CREATE INDEX IX_aps_exp_type ON aps_expectations(expected_type, emitted_at);
            CREATE INDEX IX_aps_exp_aggregate ON aps_expectations(aggregate_id, emitted_at);
        END
        """,
        """
        IF OBJECT_ID(N'dbo.aps_calendars', N'U') IS NULL
        BEGIN
            CREATE TABLE aps_calendars (
                id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                code NVARCHAR(50) NOT NULL UNIQUE,
                label NVARCHAR(255) NOT NULL
            );
        END
        """,
        """
        IF OBJECT_ID(N'dbo.aps_places', N'U') IS NULL
        BEGIN
            CREATE TABLE aps_places (
                id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                parent_id INT NULL,
                level_code NVARCHAR(30) NOT NULL,
                code NVARCHAR(80) NOT NULL UNIQUE,
                label NVARCHAR(255) NOT NULL,
                calendar_id INT NULL,
                FOREIGN KEY (parent_id) REFERENCES aps_places(id),
                FOREIGN KEY (calendar_id) REFERENCES aps_calendars(id)
            );
        END
        """,
        """
        IF OBJECT_ID(N'dbo.aps_work_regimes', N'U') IS NULL
        BEGIN
            CREATE TABLE aps_work_regimes (
                id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                code NVARCHAR(50) NOT NULL UNIQUE,
                label NVARCHAR(255) NOT NULL
            );
        END
        """,
        """
        IF OBJECT_ID(N'dbo.aps_teams', N'U') IS NULL
        BEGIN
            CREATE TABLE aps_teams (
                id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                regime_id INT NOT NULL,
                code NVARCHAR(50) NOT NULL,
                start_time TIME NOT NULL,
                end_time TIME NOT NULL,
                UNIQUE (regime_id, code),
                FOREIGN KEY (regime_id) REFERENCES aps_work_regimes(id)
            );
        END
        """,
        """
        IF OBJECT_ID(N'dbo.aps_charge_centers', N'U') IS NULL
        BEGIN
            CREATE TABLE aps_charge_centers (
                id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                place_id INT NOT NULL,
                code NVARCHAR(80) NOT NULL UNIQUE,
                label NVARCHAR(255) NOT NULL,
                resource_type NVARCHAR(30) NOT NULL,
                FOREIGN KEY (place_id) REFERENCES aps_places(id)
            );
        END
        """,
        """
        IF OBJECT_ID(N'dbo.aps_charge_posts', N'U') IS NULL
        BEGIN
            CREATE TABLE aps_charge_posts (
                id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                charge_center_id INT NOT NULL,
                code NVARCHAR(80) NOT NULL,
                label NVARCHAR(255) NOT NULL,
                UNIQUE (charge_center_id, code),
                FOREIGN KEY (charge_center_id) REFERENCES aps_charge_centers(id)
            );
        END
        """,
        """
        IF OBJECT_ID(N'dbo.aps_post_regime_assignments', N'U') IS NULL
        BEGIN
            CREATE TABLE aps_post_regime_assignments (
                id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                charge_post_id INT NOT NULL,
                regime_id INT NOT NULL,
                valid_from DATE NOT NULL,
                valid_to DATE NULL,
                FOREIGN KEY (charge_post_id) REFERENCES aps_charge_posts(id),
                FOREIGN KEY (regime_id) REFERENCES aps_work_regimes(id)
            );
        END
        """,
        """
        IF OBJECT_ID(N'dbo.aps_stock_zones', N'U') IS NULL
        BEGIN
            CREATE TABLE aps_stock_zones (
                id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                place_id INT NOT NULL,
                role NVARCHAR(30) NOT NULL,
                served_charge_center_id INT NULL,
                FOREIGN KEY (place_id) REFERENCES aps_places(id),
                FOREIGN KEY (served_charge_center_id) REFERENCES aps_charge_centers(id)
            );
        END
        """,
        """
        IF COL_LENGTH(N'dbo.articles', N'aps_decoupling_point') IS NULL
            ALTER TABLE articles ADD aps_decoupling_point NVARCHAR(30) NULL;
        """,
        """
        IF COL_LENGTH(N'dbo.articles', N'aps_traceability') IS NULL
            ALTER TABLE articles ADD aps_traceability NVARCHAR(20) NULL;
        """,
        """
        IF COL_LENGTH(N'dbo.articles', N'aps_hre_precompile') IS NULL
            ALTER TABLE articles ADD aps_hre_precompile FLOAT NULL;
        """,
        """
        IF COL_LENGTH(N'dbo.customers', N'aps_bath_compat_rule') IS NULL
            ALTER TABLE customers ADD aps_bath_compat_rule NVARCHAR(40) NULL;
        """,
        """
        IF COL_LENGTH(N'dbo.customers', N'aps_usual_engagement_month') IS NULL
            ALTER TABLE customers ADD aps_usual_engagement_month INT NULL;
        """,
        """
        IF COL_LENGTH(N'dbo.bom_base_lines', N'aps_operation_code') IS NULL
            ALTER TABLE bom_base_lines ADD aps_operation_code NVARCHAR(80) NULL;
        """,
        """
        IF COL_LENGTH(N'dbo.bom_base_lines', N'aps_bath_constraint') IS NULL
            ALTER TABLE bom_base_lines ADD aps_bath_constraint NVARCHAR(20) NULL;
        """,
        """
        IF COL_LENGTH(N'dbo.bom_bases', N'aps_valid_from') IS NULL
            ALTER TABLE bom_bases ADD aps_valid_from DATE NULL;
        """,
        """
        IF COL_LENGTH(N'dbo.bom_bases', N'aps_valid_to') IS NULL
            ALTER TABLE bom_bases ADD aps_valid_to DATE NULL;
        """,
        """
        IF OBJECT_ID(N'dbo.aps_stock_lots', N'U') IS NULL
        BEGIN
            CREATE TABLE aps_stock_lots (
                id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                article_id INT NOT NULL,
                lot_code NVARCHAR(80) NULL,
                bath_code NVARCHAR(80) NULL,
                quantity FLOAT NOT NULL,
                unit NVARCHAR(20) NOT NULL,
                status NVARCHAR(30) NOT NULL,
                stock_zone_id INT NULL,
                updated_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
                FOREIGN KEY (article_id) REFERENCES articles(id),
                FOREIGN KEY (stock_zone_id) REFERENCES aps_stock_zones(id)
            );
            CREATE INDEX IX_aps_stock_lots_article ON aps_stock_lots(article_id, status);
        END
        """,
        """
        IF OBJECT_ID(N'dbo.aps_circuits', N'U') IS NULL
        BEGIN
            CREATE TABLE aps_circuits (
                id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                code NVARCHAR(80) NOT NULL UNIQUE,
                segment_id NVARCHAR(20) NOT NULL,
                operation_code NVARCHAR(80) NULL,
                circuit_type NVARCHAR(20) NOT NULL,
                post_code NVARCHAR(80) NULL,
                supplier_code NVARCHAR(80) NULL,
                unit_time_minutes FLOAT NOT NULL DEFAULT 0,
                yield_rate FLOAT NOT NULL DEFAULT 1,
                activation_lead_days FLOAT NOT NULL DEFAULT 0,
                traversal_lead_days FLOAT NOT NULL DEFAULT 0
            );
        END
        """,
        """
        IF OBJECT_ID(N'dbo.aps_external_engagements', N'U') IS NULL
        BEGIN
            CREATE TABLE aps_external_engagements (
                id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                partner_code NVARCHAR(80) NOT NULL,
                segment_id NVARCHAR(20) NOT NULL,
                quantity FLOAT NOT NULL,
                handover_date DATE NOT NULL,
                return_date DATE NOT NULL,
                engagement_deadline DATE NOT NULL,
                status NVARCHAR(30) NOT NULL DEFAULT 'OPEN',
                residual_volume FLOAT NOT NULL
            );
        END
        """,
        """
        IF OBJECT_ID(N'dbo.aps_supplier_lead_times', N'U') IS NULL
        BEGIN
            CREATE TABLE aps_supplier_lead_times (
                id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                article_code NVARCHAR(100) NOT NULL,
                supplier_code NVARCHAR(80) NOT NULL,
                standard_lead_days FLOAT NOT NULL,
                observed_mu_days FLOAT NOT NULL,
                observed_sigma_days FLOAT NOT NULL,
                moq FLOAT NULL,
                multiple_lot FLOAT NULL,
                UNIQUE (article_code, supplier_code)
            );
        END
        """,
        """
        IF OBJECT_ID(N'dbo.aps_compiled_artifacts', N'U') IS NULL
        BEGIN
            CREATE TABLE aps_compiled_artifacts (
                id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                root_article_code NVARCHAR(100) NOT NULL,
                segment NVARCHAR(20) NOT NULL,
                circuit_chain NVARCHAR(200) NOT NULL,
                artifact_kind NVARCHAR(20) NOT NULL,
                payload_json NVARCHAR(MAX) NOT NULL,
                source_hash NVARCHAR(128) NOT NULL,
                status NVARCHAR(20) NOT NULL,
                compiled_at DATETIME2 NOT NULL CONSTRAINT DF_aps_comp_at DEFAULT SYSUTCDATETIME(),
                notes NVARCHAR(1000) NULL
            );
            CREATE INDEX IX_aps_comp_key ON aps_compiled_artifacts(root_article_code, segment, circuit_chain, artifact_kind, status);
        END
        """,
        """
        IF OBJECT_ID(N'dbo.aps_compile_source_index', N'U') IS NULL
        BEGIN
            CREATE TABLE aps_compile_source_index (
                id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                source_marker NVARCHAR(400) NOT NULL,
                root_article_code NVARCHAR(100) NOT NULL,
                segment NVARCHAR(20) NOT NULL,
                circuit_chain NVARCHAR(200) NOT NULL
            );
            CREATE INDEX IX_aps_src_marker ON aps_compile_source_index(source_marker);
        END
        """,
        """
        IF OBJECT_ID(N'dbo.aps_capacity_resources', N'U') IS NULL
        BEGIN
            CREATE TABLE aps_capacity_resources (
                id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                code NVARCHAR(80) NOT NULL UNIQUE,
                label NVARCHAR(255) NOT NULL,
                resource_type NVARCHAR(30) NOT NULL,
                unit NVARCHAR(40) NOT NULL,
                resource_count FLOAT NOT NULL,
                default_duration_hours FLOAT NOT NULL DEFAULT 8,
                rho_target FLOAT NOT NULL DEFAULT 0.85,
                confirmation_status NVARCHAR(30) NOT NULL DEFAULT 'TO_CONFIRM',
                bath_max_fill FLOAT NULL,
                bath_observed_fill FLOAT NULL,
                pcs_per_day_ref FLOAT NULL
            );
        END
        """,
        """
        IF OBJECT_ID(N'dbo.aps_capacity_unavailabilities', N'U') IS NULL
        BEGIN
            CREATE TABLE aps_capacity_unavailabilities (
                id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                resource_code NVARCHAR(80) NOT NULL,
                from_date DATE NOT NULL,
                to_date DATE NOT NULL,
                quantity FLOAT NOT NULL,
                reason NVARCHAR(200) NOT NULL
            );
        END
        """,
        """
        IF OBJECT_ID(N'dbo.aps_capacity_eta', N'U') IS NULL
        BEGIN
            CREATE TABLE aps_capacity_eta (
                id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                resource_code NVARCHAR(80) NOT NULL,
                family_code NVARCHAR(80) NULL,
                team_code NVARCHAR(50) NULL,
                eta FLOAT NOT NULL,
                UNIQUE (resource_code, family_code, team_code)
            );
        END
        """,
        """
        IF OBJECT_ID(N'dbo.aps_capacity_reservations', N'U') IS NULL
        BEGIN
            CREATE TABLE aps_capacity_reservations (
                id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                resource_code NVARCHAR(80) NOT NULL,
                bucket_date DATE NOT NULL,
                quantity FLOAT NOT NULL,
                unit NVARCHAR(40) NOT NULL,
                aggregate_id NVARCHAR(200) NOT NULL,
                reason NVARCHAR(200) NULL,
                created_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
            );
        END
        """,
        """
        IF OBJECT_ID(N'dbo.aps_elasticity_tiers', N'U') IS NULL
        BEGIN
            CREATE TABLE aps_elasticity_tiers (
                id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                code NVARCHAR(80) NOT NULL UNIQUE,
                resource_code NVARCHAR(80) NOT NULL,
                activation_lead_days FLOAT NOT NULL,
                gain FLOAT NOT NULL,
                tier_eta FLOAT NOT NULL DEFAULT 1,
                cost_future FLOAT NULL,
                unit NVARCHAR(40) NOT NULL,
                confirmation_status NVARCHAR(30) NOT NULL DEFAULT 'TO_CONFIRM'
            );
        END
        """,
        """
        IF OBJECT_ID(N'dbo.aps_segment_cbn_runs', N'U') IS NULL
        BEGIN
            CREATE TABLE aps_segment_cbn_runs (
                id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                demand_id NVARCHAR(120) NOT NULL,
                segment NVARCHAR(20) NOT NULL,
                finished_article_code NVARCHAR(100) NOT NULL,
                success BIT NOT NULL,
                error_message NVARCHAR(500) NULL,
                artifact_hash NVARCHAR(128) NULL,
                result_json NVARCHAR(MAX) NOT NULL,
                created_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
            );
        END
        """,
        """
        IF OBJECT_ID(N'dbo.tr_aps_journal_no_update', N'TR') IS NULL
        EXEC(N'CREATE TRIGGER tr_aps_journal_no_update ON aps_journal_events INSTEAD OF UPDATE AS BEGIN
            RAISERROR(N''aps_journal_events est append-only'', 16, 1); ROLLBACK; END');
        """,
        """
        IF OBJECT_ID(N'dbo.tr_aps_journal_no_delete', N'TR') IS NULL
        EXEC(N'CREATE TRIGGER tr_aps_journal_no_delete ON aps_journal_events INSTEAD OF DELETE AS BEGIN
            RAISERROR(N''aps_journal_events est append-only'', 16, 1); ROLLBACK; END');
        """,
        """
        IF OBJECT_ID(N'dbo.tr_aps_exp_no_update', N'TR') IS NULL
        EXEC(N'CREATE TRIGGER tr_aps_exp_no_update ON aps_expectations INSTEAD OF UPDATE AS BEGIN
            RAISERROR(N''aps_expectations est immuable'', 16, 1); ROLLBACK; END');
        """,
        """
        IF OBJECT_ID(N'dbo.tr_aps_exp_no_delete', N'TR') IS NULL
        EXEC(N'CREATE TRIGGER tr_aps_exp_no_delete ON aps_expectations INSTEAD OF DELETE AS BEGIN
            RAISERROR(N''aps_expectations est immuable'', 16, 1); ROLLBACK; END');
        """
    ];

    private static readonly string[] FluxDdlBatches =
    [
        """
        IF OBJECT_ID(N'dbo.aps_load_runs', N'U') IS NULL
        BEGIN
            CREATE TABLE aps_load_runs (
                id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                from_date DATE NOT NULL,
                to_date DATE NOT NULL,
                bottleneck_code NVARCHAR(80) NULL,
                result_json NVARCHAR(MAX) NOT NULL,
                published BIT NOT NULL DEFAULT 1,
                created_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
            );
        END
        """,
        """
        IF OBJECT_ID(N'dbo.aps_load_lines', N'U') IS NULL
        BEGIN
            CREATE TABLE aps_load_lines (
                id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                run_id BIGINT NOT NULL,
                resource_code NVARCHAR(80) NOT NULL,
                bucket_date DATE NOT NULL,
                capacity_type NVARCHAR(30) NOT NULL,
                unit NVARCHAR(40) NOT NULL,
                qty_to_launch FLOAT NOT NULL,
                charge FLOAT NOT NULL,
                traces NVARCHAR(MAX) NULL,
                FOREIGN KEY (run_id) REFERENCES aps_load_runs(id)
            );
        END
        """,
        """
        IF OBJECT_ID(N'dbo.aps_saturation_results', N'U') IS NULL
        BEGIN
            CREATE TABLE aps_saturation_results (
                id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                run_id BIGINT NOT NULL,
                resource_code NVARCHAR(80) NOT NULL,
                from_date DATE NOT NULL,
                to_date DATE NOT NULL,
                charge_cum FLOAT NOT NULL,
                cap_cum FLOAT NOT NULL,
                rho FLOAT NULL,
                rho_target FLOAT NOT NULL,
                status NVARCHAR(40) NOT NULL,
                FOREIGN KEY (run_id) REFERENCES aps_load_runs(id)
            );
        END
        """,
        """
        IF OBJECT_ID(N'dbo.aps_bottleneck_history', N'U') IS NULL
        BEGIN
            CREATE TABLE aps_bottleneck_history (
                id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                from_date DATE NOT NULL,
                to_date DATE NOT NULL,
                constraining_code NVARCHAR(80) NOT NULL,
                kind NVARCHAR(20) NOT NULL,
                rho FLOAT NULL,
                explanation NVARCHAR(1000) NOT NULL,
                moved BIT NOT NULL DEFAULT 0,
                created_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
            );
        END
        """,
        """
        IF OBJECT_ID(N'dbo.aps_buffer_snapshots', N'U') IS NULL
        BEGIN
            CREATE TABLE aps_buffer_snapshots (
                id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                charge_center_code NVARCHAR(80) NOT NULL,
                buffer_hre FLOAT NOT NULL,
                coverage_days FLOAT NULL,
                adequacy FLOAT NULL,
                adequacy_status NVARCHAR(30) NOT NULL,
                zone NVARCHAR(20) NULL,
                created_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
            );
        END
        """,
        """
        IF OBJECT_ID(N'dbo.aps_buffer_composition', N'U') IS NULL
        BEGIN
            CREATE TABLE aps_buffer_composition (
                id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                snapshot_id BIGINT NOT NULL,
                article_code NVARCHAR(100) NOT NULL,
                family_code NVARCHAR(80) NULL,
                quantity FLOAT NOT NULL,
                hre_per_unit FLOAT NOT NULL,
                FOREIGN KEY (snapshot_id) REFERENCES aps_buffer_snapshots(id)
            );
        END
        """,
        """
        IF OBJECT_ID(N'dbo.aps_buffer_targets', N'U') IS NULL
        BEGIN
            CREATE TABLE aps_buffer_targets (
                id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                charge_center_code NVARCHAR(80) NOT NULL UNIQUE,
                k_factor FLOAT NULL,
                reaction_lead_days FLOAT NULL,
                variance_json NVARCHAR(MAX) NULL,
                confirmation_status NVARCHAR(30) NOT NULL DEFAULT 'TO_CONFIRM'
            );
        END
        """,
        """
        IF OBJECT_ID(N'dbo.aps_space_capacity', N'U') IS NULL
        BEGIN
            CREATE TABLE aps_space_capacity (
                id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                place_code NVARCHAR(80) NOT NULL UNIQUE,
                occupied_volume FLOAT NOT NULL,
                max_volume FLOAT NOT NULL,
                confirmation_status NVARCHAR(30) NOT NULL DEFAULT 'TO_CONFIRM'
            );
        END
        """,
        """
        IF OBJECT_ID(N'dbo.aps_rope_recommendations', N'U') IS NULL
        BEGIN
            CREATE TABLE aps_rope_recommendations (
                id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                recommended_qty FLOAT NOT NULL,
                priority NVARCHAR(40) NOT NULL,
                justification NVARCHAR(1000) NOT NULL,
                creates_wo BIT NOT NULL DEFAULT 0,
                payload_json NVARCHAR(MAX) NOT NULL,
                created_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
            );
        END
        """,
        """
        IF OBJECT_ID(N'dbo.aps_expected_mix', N'U') IS NULL
        BEGIN
            CREATE TABLE aps_expected_mix (
                id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                article_code NVARCHAR(100) NOT NULL,
                share_of_mix FLOAT NOT NULL,
                source NVARCHAR(80) NOT NULL DEFAULT 'NONE'
            );
        END
        """
    ];

    private static readonly string[] CtpDdlBatches =
    [
        """
        IF OBJECT_ID(N'dbo.aps_regime_defaults', N'U') IS NULL
        BEGIN
            CREATE TABLE aps_regime_defaults (
                id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                article_category NVARCHAR(80) NULL,
                customer_category NVARCHAR(80) NULL,
                article_code NVARCHAR(100) NULL,
                customer_code NVARCHAR(100) NULL,
                period_from DATE NULL,
                period_to DATE NULL,
                regime NVARCHAR(20) NOT NULL,
                specificity_rank INT NOT NULL DEFAULT 0
            );
        END
        """,
        """
        IF OBJECT_ID(N'dbo.aps_ctp_promises', N'U') IS NULL
        BEGIN
            CREATE TABLE aps_ctp_promises (
                id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                promise_id NVARCHAR(80) NOT NULL UNIQUE,
                demand_id NVARCHAR(120) NOT NULL,
                status NVARCHAR(30) NOT NULL,
                outcome NVARCHAR(40) NOT NULL,
                proposed_date DATE NULL,
                effective_regime NVARCHAR(20) NOT NULL,
                bottleneck NVARCHAR(80) NOT NULL,
                reliability FLOAT NULL,
                reliability_status NVARCHAR(30) NOT NULL,
                hre FLOAT NULL,
                margin_density FLOAT NULL,
                float_days INT NULL,
                float_status NVARCHAR(20) NULL,
                circuit_code NVARCHAR(80) NULL,
                bath_code NVARCHAR(80) NULL,
                external_engagement NVARCHAR(80) NULL,
                payload_json NVARCHAR(MAX) NOT NULL,
                created_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
            );
        END
        """,
        """
        IF OBJECT_ID(N'dbo.aps_reservations_matiere', N'U') IS NULL
        BEGIN
            CREATE TABLE aps_reservations_matiere (
                id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                promise_id NVARCHAR(80) NOT NULL,
                article_code NVARCHAR(100) NOT NULL,
                bath_code NVARCHAR(80) NULL,
                quantity FLOAT NOT NULL,
                status NVARCHAR(30) NOT NULL DEFAULT 'ACTIVE',
                created_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
            );
        END
        """,
        """
        IF OBJECT_ID(N'dbo.aps_reservations_capacite', N'U') IS NULL
        BEGIN
            CREATE TABLE aps_reservations_capacite (
                id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                promise_id NVARCHAR(80) NOT NULL,
                resource_code NVARCHAR(80) NOT NULL,
                bucket_date DATE NOT NULL,
                quantity FLOAT NOT NULL,
                status NVARCHAR(30) NOT NULL DEFAULT 'ACTIVE',
                created_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
            );
        END
        """,
        """
        IF OBJECT_ID(N'dbo.aps_reservations_externes', N'U') IS NULL
        BEGIN
            CREATE TABLE aps_reservations_externes (
                id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                promise_id NVARCHAR(80) NOT NULL,
                engagement_code NVARCHAR(80) NOT NULL,
                quantity FLOAT NOT NULL,
                status NVARCHAR(30) NOT NULL DEFAULT 'ACTIVE',
                created_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
            );
        END
        """,
        """
        IF OBJECT_ID(N'dbo.aps_ctp_scenarios', N'U') IS NULL
        BEGIN
            CREATE TABLE aps_ctp_scenarios (
                code NVARCHAR(40) NOT NULL PRIMARY KEY,
                label NVARCHAR(200) NOT NULL,
                pf_available FLOAT NOT NULL,
                yarn_available FLOAT NOT NULL,
                yarn_bath NVARCHAR(80) NULL,
                yarn_purchase_required BIT NOT NULL,
                purchase_lead_days FLOAT NOT NULL DEFAULT 0,
                supplier_mu FLOAT NULL,
                supplier_sigma FLOAT NULL,
                cap_engageable_day FLOAT NOT NULL,
                rho_target FLOAT NOT NULL DEFAULT 0.85,
                already_loaded FLOAT NOT NULL DEFAULT 0,
                hre_per_unit FLOAT NULL,
                hre_status NVARCHAR(30) NOT NULL DEFAULT 'TO_CONFIRM',
                resource_sigma FLOAT NULL,
                use_external BIT NOT NULL DEFAULT 0,
                confirmation_status NVARCHAR(30) NOT NULL DEFAULT 'SIMULE'
            );
        END
        """
    ];

    private static readonly string[] Phase9DdlBatches =
    [
        """
        IF OBJECT_ID(N'dbo.aps_barrier_policies', N'U') IS NULL
        BEGIN
            CREATE TABLE aps_barrier_policies (
                id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                frozen_start INT NOT NULL,
                frozen_end INT NOT NULL,
                negociable_start INT NOT NULL,
                negociable_end INT NOT NULL
            );
        END
        """,
        """
        IF OBJECT_ID(N'dbo.aps_inertia_parameters', N'U') IS NULL
        BEGIN
            CREATE TABLE aps_inertia_parameters (
                id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                date_w FLOAT NOT NULL,
                load_w FLOAT NOT NULL,
                composition_w FLOAT NOT NULL,
                circuit_w FLOAT NOT NULL,
                sequence_w FLOAT NOT NULL,
                repromise_w FLOAT NOT NULL,
                reservation_w FLOAT NOT NULL
            );
        END
        """,
        """
        IF OBJECT_ID(N'dbo.aps_flow_contracts', N'U') IS NULL
        BEGIN
            CREATE TABLE aps_flow_contracts (
                id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                contract_id NVARCHAR(80) NOT NULL,
                version INT NOT NULL,
                status NVARCHAR(30) NOT NULL,
                iso_week INT NOT NULL,
                year INT NOT NULL,
                engaged_load FLOAT NOT NULL,
                bottleneck_unit NVARCHAR(40) NOT NULL,
                window_start DATE NOT NULL,
                window_end DATE NOT NULL,
                composition_json NVARCHAR(MAX) NOT NULL,
                envelopes_json NVARCHAR(MAX) NOT NULL,
                article_code NVARCHAR(100) NULL,
                family_code NVARCHAR(80) NULL,
                order_code NVARCHAR(80) NULL,
                plan_ref NVARCHAR(120) NOT NULL,
                source_hash NVARCHAR(128) NOT NULL,
                published_at DATETIME2 NOT NULL
            );
            CREATE INDEX IX_aps_fc_key ON aps_flow_contracts(contract_id, version);
        END
        """,
        """
        IF OBJECT_ID(N'dbo.aps_flow_contract_versions', N'U') IS NULL
        BEGIN
            CREATE TABLE aps_flow_contract_versions (
                id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                contract_id NVARCHAR(80) NOT NULL,
                version INT NOT NULL,
                status NVARCHAR(30) NOT NULL,
                payload_json NVARCHAR(MAX) NOT NULL,
                created_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
            );
        END
        """,
        """
        IF OBJECT_ID(N'dbo.aps_nightly_runs', N'U') IS NULL
        BEGIN
            CREATE TABLE aps_nightly_runs (
                id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                started_at DATETIME2 NOT NULL,
                finished_at DATETIME2 NULL,
                status NVARCHAR(30) NOT NULL,
                notes NVARCHAR(1000) NULL
            );
        END
        """,
        """
        IF OBJECT_ID(N'dbo.aps_nightly_run_steps', N'U') IS NULL
        BEGIN
            CREATE TABLE aps_nightly_run_steps (
                id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                run_id BIGINT NOT NULL,
                step_order INT NOT NULL,
                step_name NVARCHAR(80) NOT NULL,
                status NVARCHAR(30) NOT NULL,
                detail NVARCHAR(MAX) NOT NULL,
                at_utc DATETIME2 NOT NULL,
                FOREIGN KEY (run_id) REFERENCES aps_nightly_runs(id)
            );
        END
        """,
        """
        IF OBJECT_ID(N'dbo.aps_estimators', N'U') IS NULL
        BEGIN
            CREATE TABLE aps_estimators (
                code NVARCHAR(80) NOT NULL PRIMARY KEY,
                value FLOAT NULL,
                observation_count INT NOT NULL,
                source NVARCHAR(120) NOT NULL,
                as_of_utc DATETIME2 NOT NULL,
                confidence NVARCHAR(30) NOT NULL,
                status NVARCHAR(30) NOT NULL
            );
        END
        """,
        """
        IF OBJECT_ID(N'dbo.aps_estimator_observations', N'U') IS NULL
        BEGIN
            CREATE TABLE aps_estimator_observations (
                id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                estimator_code NVARCHAR(80) NOT NULL,
                observed_value FLOAT NULL,
                observed_at DATETIME2 NOT NULL
            );
        END
        """,
        """
        IF OBJECT_ID(N'dbo.aps_recalibration_history', N'U') IS NULL
        BEGIN
            CREATE TABLE aps_recalibration_history (
                id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                estimator_code NVARCHAR(80) NOT NULL,
                old_value FLOAT NULL,
                new_value FLOAT NULL,
                observation_count INT NOT NULL,
                source NVARCHAR(120) NOT NULL,
                as_of_utc DATETIME2 NOT NULL,
                confidence NVARCHAR(30) NOT NULL,
                status NVARCHAR(30) NOT NULL,
                reason NVARCHAR(500) NOT NULL,
                relative_change FLOAT NOT NULL,
                invalidates BIT NOT NULL
            );
        END
        """,
        """
        IF OBJECT_ID(N'dbo.aps_expectation_fact_matches', N'U') IS NULL
        BEGIN
            CREATE TABLE aps_expectation_fact_matches (
                id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                expected_id NVARCHAR(120) NULL,
                event_id BIGINT NULL,
                dimension NVARCHAR(40) NOT NULL,
                magnitude FLOAT NOT NULL,
                hre_impacted FLOAT NULL,
                gauge_zone NVARCHAR(20) NOT NULL,
                occurred_at DATETIME2 NULL,
                detected_at DATETIME2 NOT NULL
            );
        END
        """,
        """
        IF OBJECT_ID(N'dbo.aps_replay_validations', N'U') IS NULL
        BEGIN
            CREATE TABLE aps_replay_validations (
                id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                status NVARCHAR(40) NOT NULL,
                global_score FLOAT NULL,
                unlock_m2_m6 BIT NOT NULL,
                detail_json NVARCHAR(MAX) NOT NULL,
                created_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
            );
        END
        """,
        """
        IF OBJECT_ID(N'dbo.aps_nervousness_metrics', N'U') IS NULL
        BEGIN
            CREATE TABLE aps_nervousness_metrics (
                id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                period_from DATE NOT NULL,
                period_to DATE NOT NULL,
                composite_index FLOAT NOT NULL,
                metrics_json NVARCHAR(MAX) NOT NULL,
                created_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
            );
        END
        """,
        """
        IF OBJECT_ID(N'dbo.aps_plan_snapshots', N'U') IS NULL
        BEGIN
            CREATE TABLE aps_plan_snapshots (
                id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                grain NVARCHAR(30) NOT NULL,
                role_or_post NVARCHAR(80) NOT NULL,
                start_date DATE NOT NULL,
                payload_json NVARCHAR(MAX) NOT NULL,
                plan_ref NVARCHAR(120) NOT NULL,
                created_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
            );
        END
        """,
        """
        IF OBJECT_ID(N'dbo.aps_plan_projection_lines', N'U') IS NULL
        BEGIN
            CREATE TABLE aps_plan_projection_lines (
                id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                grain NVARCHAR(30) NOT NULL,
                role_or_post NVARCHAR(80) NOT NULL,
                start_date DATE NOT NULL,
                line_json NVARCHAR(MAX) NOT NULL,
                plan_ref NVARCHAR(120) NOT NULL
            );
        END
        """
    ];
}
