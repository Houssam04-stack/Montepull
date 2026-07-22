-- APS phases 5-6 : capacite nette + CBN segments (additif)
USE AxioplanMvp;
GO

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
END;
GO

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
END;
GO

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
END;
GO

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
END;
GO

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
END;
GO

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
END;
GO

-- aps_external_engagements : table phase 3/4 — rappel additif si absente
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
        status NVARCHAR(30) NOT NULL,
        residual_volume FLOAT NOT NULL
    );
END;
GO
