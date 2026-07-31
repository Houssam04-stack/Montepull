-- Schema import Montepull (staging + extension metier). Aussi cree a la volee via EnsureSchema.

IF OBJECT_ID(N'dbo.mp_import_batches', N'U') IS NULL
BEGIN
    CREATE TABLE mp_import_batches (
        id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        batch_uid UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
        origin NVARCHAR(100) NOT NULL DEFAULT N'UI',
        imported_by NVARCHAR(100) NULL,
        imported_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        status NVARCHAR(30) NOT NULL DEFAULT N'STAGING', -- STAGING, VALIDATED, PROMOTED, CANCELLED, FAILED
        dataset_code NVARCHAR(40) NOT NULL DEFAULT N'MONTEPULL_REAL',
        summary_json NVARCHAR(MAX) NULL,
        message NVARCHAR(1000) NULL
    );
END;
GO

IF OBJECT_ID(N'dbo.mp_import_files', N'U') IS NULL
BEGIN
    CREATE TABLE mp_import_files (
        id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        batch_id BIGINT NOT NULL,
        file_name NVARCHAR(255) NOT NULL,
        file_kind NVARCHAR(40) NOT NULL,
        file_hash NVARCHAR(64) NOT NULL,
        sheet_names_json NVARCHAR(MAX) NULL,
        bytes_length INT NOT NULL DEFAULT 0,
        status NVARCHAR(30) NOT NULL DEFAULT N'READ',
        FOREIGN KEY (batch_id) REFERENCES mp_import_batches(id),
        CONSTRAINT uq_mp_import_files_batch_hash UNIQUE (batch_id, file_hash)
    );
END;
GO

IF OBJECT_ID(N'dbo.mp_import_rows', N'U') IS NULL
BEGIN
    CREATE TABLE mp_import_rows (
        id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        batch_id BIGINT NOT NULL,
        file_id BIGINT NOT NULL,
        sheet_name NVARCHAR(120) NOT NULL,
        source_row_no INT NOT NULL,
        row_hash NVARCHAR(64) NOT NULL,
        status NVARCHAR(30) NOT NULL DEFAULT N'STAGED', -- STAGED, WARNING, REJECTED, PROMOTED, SKIPPED
        error_message NVARCHAR(1000) NULL,
        raw_json NVARCHAR(MAX) NULL,
        FOREIGN KEY (batch_id) REFERENCES mp_import_batches(id),
        FOREIGN KEY (file_id) REFERENCES mp_import_files(id),
        CONSTRAINT uq_mp_import_rows_hash UNIQUE (batch_id, row_hash)
    );
END;
GO

IF OBJECT_ID(N'dbo.mp_staging_commandes', N'U') IS NULL
BEGIN
    CREATE TABLE mp_staging_commandes (
        id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        batch_id BIGINT NOT NULL,
        file_id BIGINT NOT NULL,
        source_row_no INT NOT NULL,
        row_hash NVARCHAR(64) NOT NULL,
        order_code NVARCHAR(100) NOT NULL,
        customer_code NVARCHAR(50) NULL,
        article_code NVARCHAR(100) NOT NULL,
        designation NVARCHAR(255) NULL,
        qty_ordered FLOAT NOT NULL DEFAULT 0,
        qty_launched FLOAT NOT NULL DEFAULT 0,
        somme_operations FLOAT NULL,
        somme_op_70_commande FLOAT NULL,
        somme_op_70_article FLOAT NULL,
        last_operation INT NULL,
        qty_produced FLOAT NOT NULL DEFAULT 0,
        remaining_to_launch FLOAT NOT NULL DEFAULT 0,
        remaining_to_produce FLOAT NOT NULL DEFAULT 0,
        launch_status NVARCHAR(40) NULL,
        status NVARCHAR(30) NOT NULL DEFAULT N'STAGED',
        FOREIGN KEY (batch_id) REFERENCES mp_import_batches(id),
        CONSTRAINT uq_mp_staging_commandes_hash UNIQUE (batch_id, row_hash)
    );
END;
GO

IF OBJECT_ID(N'dbo.mp_staging_suivi_ops', N'U') IS NULL
BEGIN
    CREATE TABLE mp_staging_suivi_ops (
        id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        batch_id BIGINT NOT NULL,
        file_id BIGINT NOT NULL,
        sheet_name NVARCHAR(120) NOT NULL,
        source_row_no INT NOT NULL,
        row_hash NVARCHAR(64) NOT NULL,
        source_kind NVARCHAR(20) NOT NULL DEFAULT N'LISTE', -- LISTE, HIST
        of_code NVARCHAR(100) NOT NULL,
        order_code NVARCHAR(100) NULL,
        article_code NVARCHAR(100) NULL,
        designation NVARCHAR(255) NULL,
        operation_no INT NULL,
        actual_start DATETIME2 NULL,
        actual_end DATETIME2 NULL,
        qty_good FLOAT NOT NULL DEFAULT 0,
        qty_rejected FLOAT NOT NULL DEFAULT 0,
        of_quantity FLOAT NULL,
        qty_ordered FLOAT NULL,
        weight_value FLOAT NULL,
        status_text NVARCHAR(80) NULL,
        atelier NVARCHAR(100) NULL,
        packet_no NVARCHAR(50) NULL,
        customer_code NVARCHAR(50) NULL,
        customer_name NVARCHAR(255) NULL,
        order_date DATE NULL,
        delivery_date DATE NULL,
        customer_order_ref NVARCHAR(100) NULL,
        duration_seconds FLOAT NULL,
        duration_kind NVARCHAR(30) NULL, -- SCAN, PROCESS, INVALID, UNKNOWN
        status NVARCHAR(30) NOT NULL DEFAULT N'STAGED',
        FOREIGN KEY (batch_id) REFERENCES mp_import_batches(id),
        CONSTRAINT uq_mp_staging_suivi_hash UNIQUE (batch_id, row_hash)
    );
END;
GO

IF OBJECT_ID(N'dbo.mp_staging_articles', N'U') IS NULL
BEGIN
    CREATE TABLE mp_staging_articles (
        id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        batch_id BIGINT NOT NULL,
        article_code NVARCHAR(100) NOT NULL,
        designation NVARCHAR(255) NULL,
        source_file NVARCHAR(255) NULL,
        status NVARCHAR(30) NOT NULL DEFAULT N'STAGED',
        CONSTRAINT uq_mp_staging_articles UNIQUE (batch_id, article_code)
    );
END;
GO

IF OBJECT_ID(N'dbo.mp_staging_nomenclature', N'U') IS NULL
BEGIN
    CREATE TABLE mp_staging_nomenclature (
        id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        batch_id BIGINT NOT NULL,
        file_id BIGINT NOT NULL,
        source_row_no INT NOT NULL,
        row_hash NVARCHAR(64) NOT NULL,
        parent_article_code NVARCHAR(100) NOT NULL,
        component_code NVARCHAR(100) NULL,
        component_label NVARCHAR(255) NULL,
        quantity_base FLOAT NULL,
        unit NVARCHAR(20) NULL,
        supplier NVARCHAR(100) NULL,
        loss_rate FLOAT NULL,
        is_calculated BIT NOT NULL DEFAULT 0,
        raw_json NVARCHAR(MAX) NULL,
        status NVARCHAR(30) NOT NULL DEFAULT N'STAGED',
        CONSTRAINT uq_mp_staging_nomen_hash UNIQUE (batch_id, row_hash)
    );
END;
GO

IF OBJECT_ID(N'dbo.mp_staging_gamme', N'U') IS NULL
BEGIN
    CREATE TABLE mp_staging_gamme (
        id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        batch_id BIGINT NOT NULL,
        of_code NVARCHAR(100) NOT NULL,
        article_code NVARCHAR(100) NULL,
        operation_no INT NOT NULL,
        qty_at_op FLOAT NULL,
        status NVARCHAR(30) NOT NULL DEFAULT N'STAGED',
        CONSTRAINT uq_mp_staging_gamme UNIQUE (batch_id, of_code, operation_no)
    );
END;
GO

IF OBJECT_ID(N'dbo.mp_import_anomalies', N'U') IS NULL
BEGIN
    CREATE TABLE mp_import_anomalies (
        id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        batch_id BIGINT NOT NULL,
        file_id BIGINT NULL,
        sheet_name NVARCHAR(120) NULL,
        source_row_no INT NULL,
        severity NVARCHAR(20) NOT NULL, -- ERROR, WARNING, INFO
        code NVARCHAR(60) NOT NULL,
        message NVARCHAR(1000) NOT NULL,
        context_json NVARCHAR(MAX) NULL,
        created_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        FOREIGN KEY (batch_id) REFERENCES mp_import_batches(id)
    );
END;
GO

IF OBJECT_ID(N'dbo.mp_import_mappings', N'U') IS NULL
BEGIN
    CREATE TABLE mp_import_mappings (
        id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        source_operation_code NVARCHAR(40) NOT NULL,
        axioplan_operation_no INT NOT NULL,
        workcenter_code NVARCHAR(50) NULL,
        sequence_no INT NULL,
        unit NVARCHAR(20) NULL,
        event_nature NVARCHAR(40) NULL,
        is_active BIT NOT NULL DEFAULT 1,
        notes NVARCHAR(500) NULL,
        CONSTRAINT uq_mp_import_mappings_src UNIQUE (source_operation_code)
    );
END;
GO

IF OBJECT_ID(N'dbo.mp_of_operation_agg', N'U') IS NULL
BEGIN
    CREATE TABLE mp_of_operation_agg (
        id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        batch_id BIGINT NOT NULL,
        of_code NVARCHAR(100) NOT NULL,
        operation_no INT NOT NULL,
        qty_good_total FLOAT NOT NULL DEFAULT 0,
        qty_rejected_total FLOAT NOT NULL DEFAULT 0,
        first_start DATETIME2 NULL,
        last_end DATETIME2 NULL,
        packet_count INT NOT NULL DEFAULT 0,
        status_agg NVARCHAR(40) NULL,
        remaining_qty FLOAT NULL,
        CONSTRAINT uq_mp_of_op_agg UNIQUE (batch_id, of_code, operation_no)
    );
END;
GO

IF OBJECT_ID(N'dbo.mp_duration_stats', N'U') IS NULL
BEGIN
    CREATE TABLE mp_duration_stats (
        id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        batch_id BIGINT NOT NULL,
        operation_no INT NOT NULL,
        observation_count INT NOT NULL,
        mean_seconds FLOAT NULL,
        median_seconds FLOAT NULL,
        min_seconds FLOAT NULL,
        max_seconds FLOAT NULL,
        p25_seconds FLOAT NULL,
        p75_seconds FLOAT NULL,
        stddev_seconds FLOAT NULL,
        excluded_anomaly_count INT NOT NULL DEFAULT 0,
        CONSTRAINT uq_mp_duration_stats UNIQUE (batch_id, operation_no)
    );
END;
GO

IF OBJECT_ID(N'dbo.mo_production_events', N'U') IS NULL
BEGIN
    CREATE TABLE mo_production_events (
        id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        manufacturing_order_id INT NOT NULL,
        operation_no INT NULL,
        packet_no NVARCHAR(50) NULL,
        actual_start DATETIME2 NULL,
        actual_end DATETIME2 NULL,
        planned_start DATETIME2 NULL,
        planned_end DATETIME2 NULL,
        planned_quantity FLOAT NULL,
        actual_good_quantity FLOAT NOT NULL DEFAULT 0,
        actual_rejected_quantity FLOAT NOT NULL DEFAULT 0,
        remaining_quantity FLOAT NULL,
        duration_seconds FLOAT NULL,
        duration_kind NVARCHAR(30) NULL,
        data_source NVARCHAR(40) NOT NULL DEFAULT N'MONTEPULL_REAL',
        import_batch_id BIGINT NULL,
        row_hash NVARCHAR(64) NOT NULL,
        source_sheet NVARCHAR(120) NULL,
        source_row_no INT NULL,
        created_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        is_active BIT NOT NULL DEFAULT 1,
        FOREIGN KEY (manufacturing_order_id) REFERENCES manufacturing_orders(id),
        CONSTRAINT uq_mo_production_events_hash UNIQUE (row_hash)
    );
END;
GO

IF OBJECT_ID(N'dbo.mp_dataset_config', N'U') IS NULL
BEGIN
    CREATE TABLE mp_dataset_config (
        id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        active_dataset NVARCHAR(40) NOT NULL DEFAULT N'DEMO',
        updated_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        updated_by NVARCHAR(100) NULL
    );
    INSERT INTO mp_dataset_config (active_dataset) VALUES (N'DEMO');
END;
GO

-- Extensions metier (additives)
IF COL_LENGTH('sales_order_lines', 'qty_launched') IS NULL
    ALTER TABLE sales_order_lines ADD qty_launched FLOAT NULL;
IF COL_LENGTH('sales_order_lines', 'qty_produced') IS NULL
    ALTER TABLE sales_order_lines ADD qty_produced FLOAT NULL;
IF COL_LENGTH('sales_order_lines', 'last_operation') IS NULL
    ALTER TABLE sales_order_lines ADD last_operation INT NULL;
IF COL_LENGTH('sales_order_lines', 'remaining_to_launch') IS NULL
    ALTER TABLE sales_order_lines ADD remaining_to_launch FLOAT NULL;
IF COL_LENGTH('sales_order_lines', 'remaining_to_produce') IS NULL
    ALTER TABLE sales_order_lines ADD remaining_to_produce FLOAT NULL;
IF COL_LENGTH('sales_order_lines', 'delivery_date') IS NULL
    ALTER TABLE sales_order_lines ADD delivery_date DATE NULL;
IF COL_LENGTH('sales_order_lines', 'data_source') IS NULL
    ALTER TABLE sales_order_lines ADD data_source NVARCHAR(40) NULL;
IF COL_LENGTH('sales_order_lines', 'import_batch_id') IS NULL
    ALTER TABLE sales_order_lines ADD import_batch_id BIGINT NULL;
GO

IF COL_LENGTH('manufacturing_orders', 'planned_end') IS NULL
    ALTER TABLE manufacturing_orders ADD planned_end DATE NULL;
IF COL_LENGTH('manufacturing_orders', 'actual_start') IS NULL
    ALTER TABLE manufacturing_orders ADD actual_start DATETIME2 NULL;
IF COL_LENGTH('manufacturing_orders', 'actual_end') IS NULL
    ALTER TABLE manufacturing_orders ADD actual_end DATETIME2 NULL;
IF COL_LENGTH('manufacturing_orders', 'qty_good') IS NULL
    ALTER TABLE manufacturing_orders ADD qty_good FLOAT NULL;
IF COL_LENGTH('manufacturing_orders', 'qty_rejected') IS NULL
    ALTER TABLE manufacturing_orders ADD qty_rejected FLOAT NULL;
IF COL_LENGTH('manufacturing_orders', 'delivery_date') IS NULL
    ALTER TABLE manufacturing_orders ADD delivery_date DATE NULL;
IF COL_LENGTH('manufacturing_orders', 'last_operation') IS NULL
    ALTER TABLE manufacturing_orders ADD last_operation INT NULL;
IF COL_LENGTH('manufacturing_orders', 'order_code') IS NULL
    ALTER TABLE manufacturing_orders ADD order_code NVARCHAR(100) NULL;
IF COL_LENGTH('manufacturing_orders', 'data_source') IS NULL
    ALTER TABLE manufacturing_orders ADD data_source NVARCHAR(40) NULL;
IF COL_LENGTH('manufacturing_orders', 'import_batch_id') IS NULL
    ALTER TABLE manufacturing_orders ADD import_batch_id BIGINT NULL;
IF COL_LENGTH('manufacturing_orders', 'is_active') IS NULL
    ALTER TABLE manufacturing_orders ADD is_active BIT NOT NULL CONSTRAINT DF_mo_is_active DEFAULT 1;
GO

IF COL_LENGTH('sales_orders', 'data_source') IS NULL
    ALTER TABLE sales_orders ADD data_source NVARCHAR(40) NULL;
IF COL_LENGTH('sales_orders', 'import_batch_id') IS NULL
    ALTER TABLE sales_orders ADD import_batch_id BIGINT NULL;
GO
