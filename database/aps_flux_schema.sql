-- APS phase 7 : charge, saturation, goulot, buffer (additif)
USE AxioplanMvp;
GO

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
END;
GO

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
END;
GO

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
END;
GO

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
END;
GO

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
END;
GO

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
END;
GO

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
END;
GO

IF OBJECT_ID(N'dbo.aps_space_capacity', N'U') IS NULL
BEGIN
    CREATE TABLE aps_space_capacity (
        id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        place_code NVARCHAR(80) NOT NULL UNIQUE,
        occupied_volume FLOAT NOT NULL,
        max_volume FLOAT NOT NULL,
        confirmation_status NVARCHAR(30) NOT NULL DEFAULT 'TO_CONFIRM'
    );
END;
GO

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
END;
GO

IF OBJECT_ID(N'dbo.aps_expected_mix', N'U') IS NULL
BEGIN
    CREATE TABLE aps_expected_mix (
        id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        article_code NVARCHAR(100) NOT NULL,
        share_of_mix FLOAT NOT NULL,
        source NVARCHAR(80) NOT NULL DEFAULT 'NONE'
    );
END;
GO
