-- APS Axioplan — schema additif (journal, attendus, referentiel, compilateur)
-- Ne droppe aucune table existante.
-- Source: APS_Axioplan_Cahier_des_charges (ordres M7/M8 + referentiel + compilateur squelette)

USE AxioplanMvp;
GO

-- ========== JOURNAL DES FAITS (M7) ==========
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
END;
GO

-- ========== JOURNAL DES ATTENDUS (M8) ==========
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
END;
GO

-- ========== REFERENTIEL APS ==========
IF OBJECT_ID(N'dbo.aps_calendars', N'U') IS NULL
BEGIN
    CREATE TABLE aps_calendars (
        id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        code NVARCHAR(50) NOT NULL UNIQUE,
        label NVARCHAR(255) NOT NULL
    );
END;
GO

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
END;
GO

IF OBJECT_ID(N'dbo.aps_work_regimes', N'U') IS NULL
BEGIN
    CREATE TABLE aps_work_regimes (
        id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        code NVARCHAR(50) NOT NULL UNIQUE,
        label NVARCHAR(255) NOT NULL
    );
END;
GO

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
END;
GO

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
END;
GO

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
END;
GO

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
END;
GO

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
END;
GO

-- Extensions additives articles (heritees conservees)
IF COL_LENGTH(N'dbo.articles', N'aps_decoupling_point') IS NULL
    ALTER TABLE articles ADD aps_decoupling_point NVARCHAR(30) NULL;
GO
IF COL_LENGTH(N'dbo.articles', N'aps_traceability') IS NULL
    ALTER TABLE articles ADD aps_traceability NVARCHAR(20) NULL;
GO
IF COL_LENGTH(N'dbo.articles', N'aps_hre_precompile') IS NULL
    ALTER TABLE articles ADD aps_hre_precompile FLOAT NULL; -- derive compilateur, jamais saisi metier
GO

IF COL_LENGTH(N'dbo.customers', N'aps_bath_compat_rule') IS NULL
    ALTER TABLE customers ADD aps_bath_compat_rule NVARCHAR(40) NULL; -- PIECE | VETEMENT | LOT_CLIENT
GO
IF COL_LENGTH(N'dbo.customers', N'aps_usual_engagement_month') IS NULL
    ALTER TABLE customers ADD aps_usual_engagement_month INT NULL; -- TO_CONFIRM historique
GO

-- BOM : operation_id, contrainte bain, validite (colonnes heritees conservees)
IF COL_LENGTH(N'dbo.bom_base_lines', N'aps_operation_code') IS NULL
    ALTER TABLE bom_base_lines ADD aps_operation_code NVARCHAR(80) NULL;
GO
IF COL_LENGTH(N'dbo.bom_base_lines', N'aps_bath_constraint') IS NULL
    ALTER TABLE bom_base_lines ADD aps_bath_constraint NVARCHAR(20) NULL;
GO
IF COL_LENGTH(N'dbo.bom_bases', N'aps_valid_from') IS NULL
    ALTER TABLE bom_bases ADD aps_valid_from DATE NULL;
GO
IF COL_LENGTH(N'dbo.bom_bases', N'aps_valid_to') IS NULL
    ALTER TABLE bom_bases ADD aps_valid_to DATE NULL;
GO

-- stock_balances reste le solde agregé legacy ; lots/bains APS a cote
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
END;
GO

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
END;
GO

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
END;
GO

IF OBJECT_ID(N'dbo.aps_supplier_lead_times', N'U') IS NULL
BEGIN
    CREATE TABLE aps_supplier_lead_times (
        id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        article_code NVARCHAR(100) NOT NULL,
        supplier_code NVARCHAR(80) NOT NULL,
        standard_lead_days FLOAT NOT NULL,
        observed_mu_days FLOAT NOT NULL,
        observed_sigma_days FLOAT NOT NULL, -- estimateur critique CTP R3
        moq FLOAT NULL,
        multiple_lot FLOAT NULL,
        UNIQUE (article_code, supplier_code)
    );
END;
GO

-- ========== COMPILATEUR (artefacts) ==========
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
END;
GO

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
END;
GO

IF OBJECT_ID(N'dbo.tr_aps_journal_no_update', N'TR') IS NULL
EXEC(N'CREATE TRIGGER tr_aps_journal_no_update ON aps_journal_events INSTEAD OF UPDATE AS BEGIN
    RAISERROR(N''aps_journal_events est append-only'', 16, 1); ROLLBACK; END');
GO
IF OBJECT_ID(N'dbo.tr_aps_journal_no_delete', N'TR') IS NULL
EXEC(N'CREATE TRIGGER tr_aps_journal_no_delete ON aps_journal_events INSTEAD OF DELETE AS BEGIN
    RAISERROR(N''aps_journal_events est append-only'', 16, 1); ROLLBACK; END');
GO
IF OBJECT_ID(N'dbo.tr_aps_exp_no_update', N'TR') IS NULL
EXEC(N'CREATE TRIGGER tr_aps_exp_no_update ON aps_expectations INSTEAD OF UPDATE AS BEGIN
    RAISERROR(N''aps_expectations est immuable'', 16, 1); ROLLBACK; END');
GO
IF OBJECT_ID(N'dbo.tr_aps_exp_no_delete', N'TR') IS NULL
EXEC(N'CREATE TRIGGER tr_aps_exp_no_delete ON aps_expectations INSTEAD OF DELETE AS BEGIN
    RAISERROR(N''aps_expectations est immuable'', 16, 1); ROLLBACK; END');
GO
