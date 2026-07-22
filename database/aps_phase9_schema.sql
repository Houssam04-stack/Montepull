-- APS phase 9 : contrats, barrières, cycle M7, recette (additif)
USE AxioplanMvp;
GO
-- Voir aussi ApsSchemaBootstrap.Phase9DdlBatches
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
END;
GO
