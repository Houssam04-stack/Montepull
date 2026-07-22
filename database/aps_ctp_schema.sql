-- APS phase 8 : CTP déterministe (additif)
USE AxioplanMvp;
GO

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
END;
GO

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
END;
GO

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
END;
GO

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
END;
GO

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
END;
GO

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
END;
GO
