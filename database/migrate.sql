-- Migrations incrementales (conserve les donnees existantes)

USE master;
GO

IF DB_ID(N'AxioplanMvp') IS NULL
BEGIN
    RAISERROR(N'Base AxioplanMvp absente. Premiere installation : python scripts/init_db.py --reset', 16, 1);
END;
GO

USE AxioplanMvp;
GO

IF COL_LENGTH('attribute_definitions', 'is_formula_argument') IS NULL
BEGIN
    ALTER TABLE attribute_definitions ADD is_formula_argument BIT NOT NULL CONSTRAINT DF_attribute_definitions_is_formula_argument DEFAULT 0;
END;

IF COL_LENGTH('attribute_definitions', 'is_active') IS NULL
BEGIN
    ALTER TABLE attribute_definitions ADD is_active BIT NOT NULL CONSTRAINT DF_attribute_definitions_is_active DEFAULT 1;
END;

IF OBJECT_ID(N'dbo.requirement_formulas', N'U') IS NULL
BEGIN
    CREATE TABLE requirement_formulas (
        id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        product_family_id INT NOT NULL,
        target NVARCHAR(30) NOT NULL DEFAULT 'REQUIREMENT',
        expression NVARCHAR(500) NOT NULL,
        display_expression NVARCHAR(500) NOT NULL,
        apply_order_quantity BIT NOT NULL DEFAULT 1,
        status NVARCHAR(20) NOT NULL DEFAULT 'VALIDATED',
        created_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        updated_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        CONSTRAINT uq_requirement_formulas_family_target UNIQUE (product_family_id, target),
        FOREIGN KEY (product_family_id) REFERENCES product_families(id)
    );
END;
GO

-- Note : le schema Simulation MRP (simulations / sim_*) est cree automatiquement
-- au demarrage de l'app via SqlServerSimulationCbnRepository.EnsureSchemaAsync,
-- ou manuellement via : sqlcmd -i database/sim_cbn_schema.sql
GO

-- APS fondation (journal / attendus / referentiel / compilateur) — additif
-- Voir aussi database/aps_schema.sql ; aussi cree a la volee via ApsSchemaBootstrap.
PRINT N'APS: appliquer database/aps_schema.sql si pas encore deploye (ou laisser EnsureSchema).';
GO
