-- SQL Server schema for Axioplan MVP (Gammes & Nomenclatures)
-- Reinitialisation complete : supprime et recree la base (evite les conflits de FK).

USE master;
GO

IF DB_ID(N'AxioplanMvp') IS NOT NULL
BEGIN
    ALTER DATABASE AxioplanMvp SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
    DROP DATABASE AxioplanMvp;
END;
GO

CREATE DATABASE AxioplanMvp;
GO

USE AxioplanMvp;
GO

CREATE TABLE article_categories (
    id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    code NVARCHAR(50) NOT NULL UNIQUE,
    label NVARCHAR(255) NOT NULL
);

CREATE TABLE article_families (
    id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    category_id INT NOT NULL,
    code NVARCHAR(50) NOT NULL UNIQUE,
    label NVARCHAR(255) NOT NULL,
    FOREIGN KEY (category_id) REFERENCES article_categories(id)
);

CREATE TABLE customers (
    id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    code NVARCHAR(50) NOT NULL UNIQUE,
    label NVARCHAR(255) NOT NULL,
    status NVARCHAR(20) NOT NULL DEFAULT 'ACTIVE'
);

CREATE TABLE articles (
    id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    family_id INT NOT NULL,
    code NVARCHAR(100) NOT NULL UNIQUE,
    label NVARCHAR(255) NOT NULL,
    article_type NVARCHAR(20) NOT NULL CHECK (article_type IN ('FINISHED_GOOD', 'SEMI_FINISHED', 'COMPONENT', 'SERVICE')),
    default_unit NVARCHAR(20) NOT NULL,
    status NVARCHAR(20) NOT NULL DEFAULT 'ACTIVE',
    source_system NVARCHAR(50) NOT NULL DEFAULT 'LOCAL_SIMULATION',
    creation_mode NVARCHAR(20) NOT NULL DEFAULT 'MANUAL' CHECK (creation_mode IN ('MANUAL', 'CONFIGURED', 'DUPLICATED', 'IMPORTED')),
    customer_id INT NULL,
    source_article_id INT NULL,
    configuration_session_id INT NULL,
    duplication_session_id INT NULL,
    created_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    FOREIGN KEY (family_id) REFERENCES article_families(id),
    FOREIGN KEY (customer_id) REFERENCES customers(id)
);

CREATE TABLE attribute_definitions (
    id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    code NVARCHAR(50) NOT NULL UNIQUE,
    label NVARCHAR(255) NOT NULL,
    value_type NVARCHAR(20) NOT NULL CHECK (value_type IN ('TEXT', 'NUMBER', 'BOOLEAN', 'DATE', 'OPTION')),
    is_generator BIT NOT NULL DEFAULT 0,
    is_formula_argument BIT NOT NULL DEFAULT 0,
    is_active BIT NOT NULL DEFAULT 1,
    selection_mode NVARCHAR(30) NOT NULL DEFAULT 'CONTROLLED' CHECK (selection_mode IN ('CONTROLLED', 'CONTROLLED_WITH_CREATE', 'FREE_TEXT'))
);

CREATE TABLE attribute_formatting_rules (
    id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    attribute_id INT NOT NULL UNIQUE,
    trim_spaces BIT NOT NULL DEFAULT 1,
    collapse_spaces BIT NOT NULL DEFAULT 1,
    remove_internal_spaces BIT NOT NULL DEFAULT 0,
    case_rule NVARCHAR(20) NOT NULL DEFAULT 'UPPER' CHECK (case_rule IN ('UPPER', 'LOWER', 'TITLE', 'NONE')),
    strip_accents_for_code BIT NOT NULL DEFAULT 1,
    forbidden_chars NVARCHAR(100) NULL,
    FOREIGN KEY (attribute_id) REFERENCES attribute_definitions(id)
);

CREATE TABLE article_family_attributes (
    id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    family_id INT NOT NULL,
    attribute_id INT NOT NULL,
    position INT NOT NULL DEFAULT 0,
    is_visible BIT NOT NULL DEFAULT 1,
    is_required BIT NOT NULL DEFAULT 0,
    is_generator BIT NOT NULL DEFAULT 0,
    allows_multi_select BIT NOT NULL DEFAULT 0,
    allows_create BIT NOT NULL DEFAULT 0,
    UNIQUE (family_id, attribute_id),
    FOREIGN KEY (family_id) REFERENCES article_families(id),
    FOREIGN KEY (attribute_id) REFERENCES attribute_definitions(id)
);

CREATE TABLE customer_article_family_configurations (
    id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    customer_id INT NOT NULL,
    family_id INT NOT NULL,
    season_code NVARCHAR(50) NULL,
    status NVARCHAR(20) NOT NULL DEFAULT 'ACTIVE',
    UNIQUE (customer_id, family_id, season_code),
    FOREIGN KEY (customer_id) REFERENCES customers(id),
    FOREIGN KEY (family_id) REFERENCES article_families(id)
);

CREATE TABLE customer_article_family_attributes (
    id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    configuration_id INT NOT NULL,
    attribute_id INT NOT NULL,
    is_visible BIT NOT NULL DEFAULT 1,
    is_required BIT NOT NULL DEFAULT 0,
    is_modifiable BIT NOT NULL DEFAULT 1,
    allows_multi_select BIT NOT NULL DEFAULT 0,
    allows_create BIT NOT NULL DEFAULT 0,
    is_generator BIT NOT NULL DEFAULT 0,
    role_in_code BIT NOT NULL DEFAULT 0,
    role_in_description BIT NOT NULL DEFAULT 0,
    role_in_bom BIT NOT NULL DEFAULT 0,
    role_in_pegging BIT NOT NULL DEFAULT 0,
    validation_status NVARCHAR(20) NOT NULL DEFAULT 'VALIDATED' CHECK (validation_status IN ('DRAFT', 'TO_VALIDATE', 'VALIDATED', 'BLOCKED', 'MERGED')),
    UNIQUE (configuration_id, attribute_id),
    FOREIGN KEY (configuration_id) REFERENCES customer_article_family_configurations(id),
    FOREIGN KEY (attribute_id) REFERENCES attribute_definitions(id)
);

CREATE TABLE configuration_sessions (
    id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    customer_id INT NULL,
    family_id INT NOT NULL,
    configuration_id INT NULL,
    status NVARCHAR(20) NOT NULL DEFAULT 'COMPLETED' CHECK (status IN ('DRAFT', 'IN_PROGRESS', 'COMPLETED', 'CANCELLED', 'ERROR')),
    created_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    notes NVARCHAR(MAX) NULL,
    bom_link_status NVARCHAR(30) NULL CHECK (bom_link_status IS NULL OR bom_link_status IN ('NOT_LINKED', 'LINKED', 'TO_RECALCULATE', 'A_CONFIRMER')),
    FOREIGN KEY (customer_id) REFERENCES customers(id),
    FOREIGN KEY (family_id) REFERENCES article_families(id),
    FOREIGN KEY (configuration_id) REFERENCES customer_article_family_configurations(id)
);

CREATE TABLE article_duplication_sessions (
    id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    source_article_id INT NOT NULL,
    new_article_code NVARCHAR(100) NOT NULL,
    status NVARCHAR(20) NOT NULL DEFAULT 'COMPLETED' CHECK (status IN ('DRAFT', 'IN_PROGRESS', 'COMPLETED', 'CANCELLED', 'ERROR')),
    copy_bom BIT NOT NULL DEFAULT 0,
    copy_routing BIT NOT NULL DEFAULT 0,
    created_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    notes NVARCHAR(MAX) NULL,
    FOREIGN KEY (source_article_id) REFERENCES articles(id)
);

CREATE TABLE attribute_options (
    id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    attribute_id INT NOT NULL,
    display_value NVARCHAR(255) NOT NULL,
    normalized_value NVARCHAR(255) NOT NULL,
    technical_code NVARCHAR(50) NOT NULL,
    status NVARCHAR(20) NOT NULL DEFAULT 'VALIDATED',
    UNIQUE (attribute_id, normalized_value),
    FOREIGN KEY (attribute_id) REFERENCES attribute_definitions(id)
);

CREATE TABLE article_attribute_values (
    id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    article_id INT NOT NULL,
    attribute_id INT NOT NULL,
    option_id INT NULL,
    raw_value NVARCHAR(255) NULL,
    display_value NVARCHAR(255) NOT NULL,
    normalized_value NVARCHAR(255) NOT NULL,
    technical_code NVARCHAR(100) NOT NULL,
    UNIQUE (article_id, attribute_id, normalized_value),
    FOREIGN KEY (article_id) REFERENCES articles(id),
    FOREIGN KEY (attribute_id) REFERENCES attribute_definitions(id),
    FOREIGN KEY (option_id) REFERENCES attribute_options(id)
);

CREATE TABLE configuration_inputs (
    id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    session_id INT NOT NULL,
    attribute_id INT NOT NULL,
    option_id INT NULL,
    raw_value NVARCHAR(255) NULL,
    display_value NVARCHAR(255) NOT NULL,
    normalized_value NVARCHAR(255) NOT NULL,
    technical_code NVARCHAR(100) NOT NULL,
    generated_article_id INT NULL,
    FOREIGN KEY (session_id) REFERENCES configuration_sessions(id),
    FOREIGN KEY (attribute_id) REFERENCES attribute_definitions(id),
    FOREIGN KEY (option_id) REFERENCES attribute_options(id),
    FOREIGN KEY (generated_article_id) REFERENCES articles(id)
);

CREATE TABLE article_duplication_changes (
    id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    session_id INT NOT NULL,
    attribute_id INT NOT NULL,
    old_display_value NVARCHAR(255) NULL,
    new_display_value NVARCHAR(255) NULL,
    old_normalized_value NVARCHAR(255) NULL,
    new_normalized_value NVARCHAR(255) NULL,
    FOREIGN KEY (session_id) REFERENCES article_duplication_sessions(id),
    FOREIGN KEY (attribute_id) REFERENCES attribute_definitions(id)
);

ALTER TABLE articles ADD CONSTRAINT FK_articles_source_article
    FOREIGN KEY (source_article_id) REFERENCES articles(id);
ALTER TABLE articles ADD CONSTRAINT FK_articles_configuration_session
    FOREIGN KEY (configuration_session_id) REFERENCES configuration_sessions(id);
ALTER TABLE articles ADD CONSTRAINT FK_articles_duplication_session
    FOREIGN KEY (duplication_session_id) REFERENCES article_duplication_sessions(id);

CREATE TABLE product_families (
    id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    article_family_id INT NOT NULL,
    code NVARCHAR(50) NOT NULL UNIQUE,
    label NVARCHAR(255) NOT NULL,
    FOREIGN KEY (article_family_id) REFERENCES article_families(id)
);

CREATE TABLE workcenters (
    id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    code NVARCHAR(50) NOT NULL UNIQUE,
    label NVARCHAR(255) NOT NULL,
    source_system NVARCHAR(50) NOT NULL DEFAULT 'LOCAL_SIMULATION'
);

CREATE TABLE units_of_measure (
    id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    code NVARCHAR(20) NOT NULL UNIQUE,
    label NVARCHAR(255) NOT NULL,
    unit_type NVARCHAR(20) NOT NULL CHECK (unit_type IN ('COUNT', 'WEIGHT', 'LENGTH', 'TIME', 'VOLUME', 'OTHER')),
    source_status NVARCHAR(20) NOT NULL DEFAULT 'SIMULATED' CHECK (source_status IN ('CONFIRMED', 'SIMULATED', 'HYPOTHESIS'))
);

CREATE TABLE unit_conversion_rules (
    id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    from_unit_id INT NOT NULL,
    to_unit_id INT NOT NULL,
    factor FLOAT NOT NULL,
    source_status NVARCHAR(20) NOT NULL DEFAULT 'SIMULATED' CHECK (source_status IN ('CONFIRMED', 'SIMULATED', 'HYPOTHESIS')),
    notes NVARCHAR(MAX) NULL,
    UNIQUE (from_unit_id, to_unit_id),
    FOREIGN KEY (from_unit_id) REFERENCES units_of_measure(id),
    FOREIGN KEY (to_unit_id) REFERENCES units_of_measure(id)
);

CREATE TABLE bom_bases (
    id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    product_family_id INT NOT NULL,
    code NVARCHAR(100) NOT NULL UNIQUE,
    version INT NOT NULL DEFAULT 1,
    status NVARCHAR(20) NOT NULL DEFAULT 'DRAFT',
    FOREIGN KEY (product_family_id) REFERENCES product_families(id)
);

CREATE TABLE bom_base_lines (
    id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    bom_base_id INT NOT NULL,
    line_no INT NOT NULL,
    component_article_id INT NOT NULL,
    quantity_base FLOAT NOT NULL,
    unit NVARCHAR(20) NOT NULL,
    loss_rate FLOAT NOT NULL DEFAULT 0,
    behavior NVARCHAR(20) NOT NULL CHECK (behavior IN ('FIXED', 'CALCULATED', 'REPLACED', 'CONDITIONAL', 'MANUAL', 'SELECTED', 'COPY_TO_VALIDATE')),
    UNIQUE (bom_base_id, line_no),
    FOREIGN KEY (bom_base_id) REFERENCES bom_bases(id),
    FOREIGN KEY (component_article_id) REFERENCES articles(id)
);

CREATE TABLE bom_line_generation_rules (
    id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    bom_base_line_id INT NOT NULL,
    rule_code NVARCHAR(100) NOT NULL,
    behavior NVARCHAR(20) NOT NULL CHECK (behavior IN ('FIXED', 'CALCULATED', 'REPLACED', 'CONDITIONAL', 'MANUAL', 'SELECTED', 'COPY_TO_VALIDATE')),
    missing_rule_strategy NVARCHAR(30) NOT NULL DEFAULT 'COPY_BASE' CHECK (missing_rule_strategy IN ('COPY_BASE', 'MANUAL_INPUT', 'SELECT_VALUE', 'DEFAULT_VALUE', 'BLOCK_GENERATION', 'MARK_TO_COMPLETE')),
    status NVARCHAR(20) NOT NULL DEFAULT 'DRAFT' CHECK (status IN ('DRAFT', 'TO_VALIDATE', 'VALIDATED', 'BLOCKED', 'OBSOLETE')),
    notes NVARCHAR(MAX) NULL,
    UNIQUE (bom_base_line_id, rule_code),
    FOREIGN KEY (bom_base_line_id) REFERENCES bom_base_lines(id)
);

CREATE TABLE routing_bases (
    id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    product_family_id INT NOT NULL,
    code NVARCHAR(100) NOT NULL UNIQUE,
    version INT NOT NULL DEFAULT 1,
    status NVARCHAR(20) NOT NULL DEFAULT 'DRAFT',
    FOREIGN KEY (product_family_id) REFERENCES product_families(id)
);

CREATE TABLE routing_base_operations (
    id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    routing_base_id INT NOT NULL,
    operation_no INT NOT NULL,
    name NVARCHAR(255) NOT NULL,
    workcenter_id INT NOT NULL,
    quantity_base FLOAT NOT NULL,
    time_base FLOAT NOT NULL,
    time_unit NVARCHAR(20) NOT NULL DEFAULT 'TO_CONFIRM',
    behavior NVARCHAR(20) NOT NULL DEFAULT 'COPY_TO_VALIDATE',
    UNIQUE (routing_base_id, operation_no),
    FOREIGN KEY (routing_base_id) REFERENCES routing_bases(id),
    FOREIGN KEY (workcenter_id) REFERENCES workcenters(id)
);

CREATE TABLE routing_operation_generation_rules (
    id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    routing_base_operation_id INT NOT NULL,
    rule_code NVARCHAR(100) NOT NULL,
    behavior NVARCHAR(20) NOT NULL DEFAULT 'COPY_TO_VALIDATE' CHECK (behavior IN ('FIXED', 'CALCULATED', 'REPLACED', 'CONDITIONAL', 'MANUAL', 'SELECTED', 'COPY_TO_VALIDATE')),
    missing_rule_strategy NVARCHAR(30) NOT NULL DEFAULT 'COPY_BASE' CHECK (missing_rule_strategy IN ('COPY_BASE', 'MANUAL_INPUT', 'SELECT_VALUE', 'DEFAULT_VALUE', 'BLOCK_GENERATION', 'MARK_TO_COMPLETE')),
    status NVARCHAR(20) NOT NULL DEFAULT 'DRAFT' CHECK (status IN ('DRAFT', 'TO_VALIDATE', 'VALIDATED', 'BLOCKED', 'OBSOLETE')),
    notes NVARCHAR(MAX) NULL,
    UNIQUE (routing_base_operation_id, rule_code),
    FOREIGN KEY (routing_base_operation_id) REFERENCES routing_base_operations(id)
);

CREATE TABLE generation_profiles (
    id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    product_family_id INT NOT NULL,
    code NVARCHAR(50) NOT NULL UNIQUE,
    label NVARCHAR(255) NOT NULL,
    status NVARCHAR(20) NOT NULL CHECK (status IN ('DRAFT', 'TO_VALIDATE', 'VALIDATED', 'BLOCKED', 'OBSOLETE')),
    can_generate_bom BIT NOT NULL DEFAULT 1,
    can_generate_routing BIT NOT NULL DEFAULT 1,
    FOREIGN KEY (product_family_id) REFERENCES product_families(id)
);
GO

CREATE VIEW usable_generation_profiles AS
SELECT *
FROM generation_profiles
WHERE status = 'VALIDATED';
GO

CREATE TABLE generation_profile_dimensions (
    id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    profile_id INT NOT NULL,
    attribute_id INT NOT NULL,
    position INT NOT NULL,
    is_required BIT NOT NULL DEFAULT 1,
    UNIQUE (profile_id, attribute_id),
    FOREIGN KEY (profile_id) REFERENCES generation_profiles(id),
    FOREIGN KEY (attribute_id) REFERENCES attribute_definitions(id)
);

CREATE TABLE consumption_coefficients (
    id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    product_family_id INT NOT NULL,
    attribute_id INT NOT NULL,
    option_id INT NULL,
    raw_value NVARCHAR(255) NULL,
    coefficient FLOAT NOT NULL DEFAULT 1,
    source_status NVARCHAR(20) NOT NULL DEFAULT 'SIMULATED' CHECK (source_status IN ('CONFIRMED', 'SIMULATED', 'HYPOTHESIS')),
    status NVARCHAR(20) NOT NULL DEFAULT 'DRAFT' CHECK (status IN ('DRAFT', 'TO_VALIDATE', 'VALIDATED', 'BLOCKED', 'OBSOLETE')),
    notes NVARCHAR(MAX) NULL,
    FOREIGN KEY (product_family_id) REFERENCES product_families(id),
    FOREIGN KEY (attribute_id) REFERENCES attribute_definitions(id),
    FOREIGN KEY (option_id) REFERENCES attribute_options(id)
);

CREATE TABLE time_coefficients (
    id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    product_family_id INT NOT NULL,
    attribute_id INT NOT NULL,
    option_id INT NULL,
    raw_value NVARCHAR(255) NULL,
    coefficient FLOAT NOT NULL DEFAULT 1,
    source_status NVARCHAR(20) NOT NULL DEFAULT 'SIMULATED' CHECK (source_status IN ('CONFIRMED', 'SIMULATED', 'HYPOTHESIS')),
    status NVARCHAR(20) NOT NULL DEFAULT 'DRAFT' CHECK (status IN ('DRAFT', 'TO_VALIDATE', 'VALIDATED', 'BLOCKED', 'OBSOLETE')),
    notes NVARCHAR(MAX) NULL,
    FOREIGN KEY (product_family_id) REFERENCES product_families(id),
    FOREIGN KEY (attribute_id) REFERENCES attribute_definitions(id),
    FOREIGN KEY (option_id) REFERENCES attribute_options(id)
);

CREATE TABLE requirement_formulas (
    id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    product_family_id INT NOT NULL,
    target NVARCHAR(30) NOT NULL DEFAULT 'REQUIREMENT' CHECK (target IN ('REQUIREMENT', 'TIME')),
    expression NVARCHAR(500) NOT NULL,
    display_expression NVARCHAR(500) NOT NULL,
    apply_order_quantity BIT NOT NULL DEFAULT 1,
    status NVARCHAR(20) NOT NULL DEFAULT 'VALIDATED' CHECK (status IN ('DRAFT', 'VALIDATED', 'BLOCKED')),
    created_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    updated_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    UNIQUE (product_family_id, target),
    FOREIGN KEY (product_family_id) REFERENCES product_families(id)
);

CREATE TABLE generation_sessions (
    id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    profile_id INT NOT NULL,
    mode NVARCHAR(20) NOT NULL CHECK (mode IN ('SIMULATION', 'GENERATION')),
    status NVARCHAR(20) NOT NULL DEFAULT 'DRAFT',
    created_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    notes NVARCHAR(MAX) NULL,
    FOREIGN KEY (profile_id) REFERENCES generation_profiles(id)
);

CREATE TABLE generated_variants (
    id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    session_id INT NOT NULL,
    article_code NVARCHAR(100) NOT NULL,
    size_code NVARCHAR(50) NULL,
    color_code NVARCHAR(50) NULL,
    customer_order_code NVARCHAR(100) NULL,
    composition_code NVARCHAR(100) NULL,
    status NVARCHAR(20) NOT NULL DEFAULT 'GENERATED',
    UNIQUE (session_id, article_code),
    FOREIGN KEY (session_id) REFERENCES generation_sessions(id)
);

CREATE TABLE proposed_components (
    id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    variant_id INT NOT NULL,
    component_article_id INT NOT NULL,
    proposal_reason NVARCHAR(255) NOT NULL,
    status NVARCHAR(20) NOT NULL DEFAULT 'PROPOSED' CHECK (status IN ('PROPOSED', 'ACCEPTED', 'REPLACED', 'REFUSED', 'TO_VALIDATE')),
    source_rule_code NVARCHAR(100) NULL,
    created_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    FOREIGN KEY (variant_id) REFERENCES generated_variants(id),
    FOREIGN KEY (component_article_id) REFERENCES articles(id)
);

CREATE TABLE bom_generated_versions (
    id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    variant_id INT NOT NULL,
    source_bom_base_id INT NOT NULL,
    version INT NOT NULL DEFAULT 1,
    status NVARCHAR(20) NOT NULL DEFAULT 'GENERATED',
    FOREIGN KEY (variant_id) REFERENCES generated_variants(id),
    FOREIGN KEY (source_bom_base_id) REFERENCES bom_bases(id)
);

CREATE TABLE bom_generated_lines (
    id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    bom_generated_version_id INT NOT NULL,
    source_line_id INT NULL,
    line_no INT NOT NULL,
    component_article_id INT NOT NULL,
    quantity_net FLOAT NOT NULL,
    quantity_gross FLOAT NOT NULL,
    unit NVARCHAR(20) NOT NULL,
    loss_rate FLOAT NOT NULL DEFAULT 0,
    status NVARCHAR(20) NOT NULL DEFAULT 'GENERATED',
    FOREIGN KEY (bom_generated_version_id) REFERENCES bom_generated_versions(id),
    FOREIGN KEY (source_line_id) REFERENCES bom_base_lines(id),
    FOREIGN KEY (component_article_id) REFERENCES articles(id)
);

CREATE TABLE routing_generated_versions (
    id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    variant_id INT NOT NULL,
    source_routing_base_id INT NOT NULL,
    version INT NOT NULL DEFAULT 1,
    status NVARCHAR(20) NOT NULL DEFAULT 'GENERATED',
    FOREIGN KEY (variant_id) REFERENCES generated_variants(id),
    FOREIGN KEY (source_routing_base_id) REFERENCES routing_bases(id)
);

CREATE TABLE routing_generated_operations (
    id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    routing_generated_version_id INT NOT NULL,
    source_operation_id INT NULL,
    operation_no INT NOT NULL,
    name NVARCHAR(255) NOT NULL,
    workcenter_id INT NOT NULL,
    quantity FLOAT NOT NULL,
    time_value FLOAT NOT NULL,
    time_unit NVARCHAR(20) NOT NULL DEFAULT 'TO_CONFIRM',
    status NVARCHAR(20) NOT NULL DEFAULT 'GENERATED',
    FOREIGN KEY (routing_generated_version_id) REFERENCES routing_generated_versions(id),
    FOREIGN KEY (source_operation_id) REFERENCES routing_base_operations(id),
    FOREIGN KEY (workcenter_id) REFERENCES workcenters(id)
);

CREATE TABLE flattened_bom_lines (
    id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    bom_generated_version_id INT NOT NULL,
    generation_session_id INT NULL,
    component_article_id INT NOT NULL,
    total_quantity_net FLOAT NOT NULL,
    total_quantity_gross FLOAT NOT NULL,
    unit NVARCHAR(20) NOT NULL,
    status NVARCHAR(30) NOT NULL DEFAULT 'GENERATED' CHECK (status IN ('DRAFT', 'GENERATED', 'GENERATED_WITH_WARNINGS', 'TO_COMPLETE', 'TO_VALIDATE', 'VALIDATED', 'OBSOLETE', 'ERROR')),
    calculated_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    source_version NVARCHAR(100) NULL,
    recalculation_reason NVARCHAR(255) NULL,
    source_path NVARCHAR(255) NOT NULL,
    FOREIGN KEY (bom_generated_version_id) REFERENCES bom_generated_versions(id),
    FOREIGN KEY (generation_session_id) REFERENCES generation_sessions(id),
    FOREIGN KEY (component_article_id) REFERENCES articles(id)
);

CREATE TABLE flattened_routing_lines (
    id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    routing_generated_version_id INT NOT NULL,
    generation_session_id INT NULL,
    workcenter_id INT NOT NULL,
    total_time FLOAT NOT NULL,
    time_unit NVARCHAR(20) NOT NULL DEFAULT 'TO_CONFIRM',
    status NVARCHAR(30) NOT NULL DEFAULT 'GENERATED' CHECK (status IN ('DRAFT', 'GENERATED', 'GENERATED_WITH_WARNINGS', 'TO_COMPLETE', 'TO_VALIDATE', 'VALIDATED', 'OBSOLETE', 'ERROR')),
    calculated_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    source_version NVARCHAR(100) NULL,
    recalculation_reason NVARCHAR(255) NULL,
    source_path NVARCHAR(255) NOT NULL,
    FOREIGN KEY (routing_generated_version_id) REFERENCES routing_generated_versions(id),
    FOREIGN KEY (generation_session_id) REFERENCES generation_sessions(id),
    FOREIGN KEY (workcenter_id) REFERENCES workcenters(id)
);

CREATE TABLE calculation_traces (
    id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    session_id INT NOT NULL,
    variant_id INT NULL,
    object_type NVARCHAR(50) NOT NULL,
    object_id INT NOT NULL,
    rule_code NVARCHAR(100) NOT NULL,
    source_table NVARCHAR(100) NULL,
    source_id INT NULL,
    source_value FLOAT NULL,
    source_unit NVARCHAR(20) NULL,
    size_coefficient FLOAT NULL,
    color_coefficient FLOAT NULL,
    other_coefficient FLOAT NULL,
    loss_rate FLOAT NULL,
    result_value FLOAT NULL,
    result_unit NVARCHAR(20) NULL,
    formula NVARCHAR(255) NULL,
    details NVARCHAR(MAX) NULL,
    created_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    FOREIGN KEY (session_id) REFERENCES generation_sessions(id),
    FOREIGN KEY (variant_id) REFERENCES generated_variants(id)
);

CREATE TABLE generation_alerts (
    id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    session_id INT NOT NULL,
    severity NVARCHAR(20) NOT NULL CHECK (severity IN ('INFO', 'WARNING', 'ERROR')),
    message NVARCHAR(MAX) NOT NULL,
    object_type NVARCHAR(50) NULL,
    object_id INT NULL,
    created_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    FOREIGN KEY (session_id) REFERENCES generation_sessions(id)
);

CREATE TABLE mvp_parameter_groups (
    id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    code NVARCHAR(50) NOT NULL UNIQUE,
    label NVARCHAR(255) NOT NULL,
    position INT NOT NULL DEFAULT 0
);

CREATE TABLE mvp_parameters (
    id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    group_id INT NOT NULL,
    code NVARCHAR(100) NOT NULL UNIQUE,
    label NVARCHAR(255) NOT NULL,
    value_type NVARCHAR(20) NOT NULL CHECK (value_type IN ('TEXT', 'NUMBER', 'BOOLEAN')),
    scope NVARCHAR(20) NOT NULL DEFAULT 'GLOBAL' CHECK (scope IN ('GLOBAL', 'FAMILY', 'PROFILE')),
    description NVARCHAR(MAX) NULL,
    FOREIGN KEY (group_id) REFERENCES mvp_parameter_groups(id)
);

CREATE TABLE mvp_parameter_values (
    id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    parameter_id INT NOT NULL,
    scope_key NVARCHAR(100) NULL,
    value_text NVARCHAR(MAX) NOT NULL,
    updated_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    UNIQUE (parameter_id, scope_key),
    FOREIGN KEY (parameter_id) REFERENCES mvp_parameters(id)
);

CREATE TABLE sales_orders (
    id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    code NVARCHAR(100) NOT NULL UNIQUE,
    customer_code NVARCHAR(50) NULL,
    label NVARCHAR(255) NULL,
    status NVARCHAR(20) NOT NULL DEFAULT 'OPEN' CHECK (status IN ('DRAFT', 'OPEN', 'CLOSED', 'CANCELLED')),
    order_date DATE NULL,
    created_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
);

CREATE TABLE sales_order_lines (
    id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    sales_order_id INT NOT NULL,
    line_no INT NOT NULL,
    article_id INT NOT NULL,
    size_option_id INT NULL,
    color_option_id INT NULL,
    quantity FLOAT NOT NULL,
    unit NVARCHAR(20) NOT NULL DEFAULT 'PIECE',
    product_family_id INT NULL,
    external_ref NVARCHAR(100) NULL,
    UNIQUE (sales_order_id, line_no),
    FOREIGN KEY (sales_order_id) REFERENCES sales_orders(id),
    FOREIGN KEY (article_id) REFERENCES articles(id),
    FOREIGN KEY (size_option_id) REFERENCES attribute_options(id),
    FOREIGN KEY (color_option_id) REFERENCES attribute_options(id),
    FOREIGN KEY (product_family_id) REFERENCES product_families(id)
);

CREATE TABLE cbn_runs (
    id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    sales_order_id INT NOT NULL,
    product_family_id INT NOT NULL,
    status NVARCHAR(20) NOT NULL DEFAULT 'COMPLETED' CHECK (status IN ('DRAFT', 'RUNNING', 'COMPLETED', 'ERROR')),
    created_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    notes NVARCHAR(MAX) NULL,
    FOREIGN KEY (sales_order_id) REFERENCES sales_orders(id),
    FOREIGN KEY (product_family_id) REFERENCES product_families(id)
);

CREATE TABLE article_bom_assignments (
    id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    article_id INT NOT NULL UNIQUE,
    bom_base_id INT NOT NULL,
    FOREIGN KEY (article_id) REFERENCES articles(id),
    FOREIGN KEY (bom_base_id) REFERENCES bom_bases(id)
);

CREATE TABLE import_sessions (
    id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    import_target NVARCHAR(20) NOT NULL CHECK (import_target IN ('ORDER', 'BOM')),
    file_name NVARCHAR(255) NOT NULL,
    sheet_name NVARCHAR(255) NOT NULL,
    product_family_code NVARCHAR(50) NOT NULL,
    imported_row_count INT NOT NULL DEFAULT 0,
    status NVARCHAR(20) NOT NULL DEFAULT 'COMPLETED',
    created_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
);

CREATE TABLE import_session_warnings (
    id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    import_session_id INT NOT NULL,
    warning_message NVARCHAR(1000) NOT NULL,
    created_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    FOREIGN KEY (import_session_id) REFERENCES import_sessions(id)
);

CREATE TABLE import_mapping_profiles (
    id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    import_target NVARCHAR(20) NOT NULL,
    structure_fingerprint NVARCHAR(300) NOT NULL,
    profile_name NVARCHAR(255) NOT NULL,
    header_row INT NOT NULL,
    data_start_row INT NOT NULL,
    mappings_json NVARCHAR(MAX) NOT NULL,
    created_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    updated_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT uq_import_mapping_profiles_target_fingerprint UNIQUE (import_target, structure_fingerprint)
);

CREATE TABLE purchase_orders (
    id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    code NVARCHAR(100) NOT NULL UNIQUE,
    supplier_code NVARCHAR(50) NULL,
    label NVARCHAR(255) NULL,
    status NVARCHAR(20) NOT NULL DEFAULT 'OPEN' CHECK (status IN ('DRAFT', 'OPEN', 'CLOSED', 'CANCELLED')),
    order_date DATE NULL,
    created_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
);

CREATE TABLE purchase_order_lines (
    id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    purchase_order_id INT NOT NULL,
    line_no INT NOT NULL,
    article_id INT NOT NULL,
    quantity FLOAT NOT NULL,
    unit NVARCHAR(20) NOT NULL DEFAULT 'PIECE',
    expected_date DATE NULL,
    UNIQUE (purchase_order_id, line_no),
    FOREIGN KEY (purchase_order_id) REFERENCES purchase_orders(id),
    FOREIGN KEY (article_id) REFERENCES articles(id)
);

CREATE TABLE manufacturing_orders (
    id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    code NVARCHAR(100) NOT NULL UNIQUE,
    article_id INT NOT NULL,
    quantity FLOAT NOT NULL,
    unit NVARCHAR(20) NOT NULL DEFAULT 'PIECE',
    status NVARCHAR(20) NOT NULL DEFAULT 'PLANNED' CHECK (status IN ('DRAFT', 'PLANNED', 'RELEASED', 'CLOSED', 'CANCELLED')),
    sales_order_line_id INT NULL,
    planned_start DATE NULL,
    created_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    FOREIGN KEY (article_id) REFERENCES articles(id),
    FOREIGN KEY (sales_order_line_id) REFERENCES sales_order_lines(id)
);

CREATE TABLE manufacturing_order_lines (
    id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    manufacturing_order_id INT NOT NULL,
    line_no INT NOT NULL,
    operation_no INT NULL,
    workcenter_id INT NULL,
    quantity FLOAT NOT NULL,
    unit NVARCHAR(20) NOT NULL DEFAULT 'PIECE',
    UNIQUE (manufacturing_order_id, line_no),
    FOREIGN KEY (manufacturing_order_id) REFERENCES manufacturing_orders(id),
    FOREIGN KEY (workcenter_id) REFERENCES workcenters(id)
);

CREATE TABLE stock_balances (
    id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    article_id INT NOT NULL UNIQUE,
    quantity_available FLOAT NOT NULL DEFAULT 0,
    unit NVARCHAR(20) NOT NULL,
    updated_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    FOREIGN KEY (article_id) REFERENCES articles(id)
);

CREATE TABLE cbn_flattened_bom_lines (
    id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    cbn_run_id INT NOT NULL,
    parent_article_id INT NOT NULL,
    component_article_id INT NOT NULL,
    bom_line_no INT NOT NULL,
    bom_level INT NOT NULL DEFAULT 1,
    quantity_per_unit FLOAT NOT NULL,
    unit NVARCHAR(20) NOT NULL,
    loss_rate FLOAT NOT NULL DEFAULT 0,
    behavior NVARCHAR(20) NOT NULL,
    source_path NVARCHAR(255) NOT NULL,
    FOREIGN KEY (cbn_run_id) REFERENCES cbn_runs(id),
    FOREIGN KEY (parent_article_id) REFERENCES articles(id),
    FOREIGN KEY (component_article_id) REFERENCES articles(id)
);

CREATE TABLE cbn_material_requirements (
    id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    cbn_run_id INT NOT NULL,
    sales_order_line_id INT NULL,
    component_article_id INT NOT NULL,
    variant_label NVARCHAR(100) NULL,
    quantity_net FLOAT NOT NULL,
    quantity_gross FLOAT NOT NULL,
    unit NVARCHAR(20) NOT NULL,
    source_path NVARCHAR(255) NOT NULL,
    FOREIGN KEY (cbn_run_id) REFERENCES cbn_runs(id),
    FOREIGN KEY (sales_order_line_id) REFERENCES sales_order_lines(id),
    FOREIGN KEY (component_article_id) REFERENCES articles(id)
);

CREATE TABLE cbn_calculation_traces (
    id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    cbn_run_id INT NOT NULL,
    sales_order_line_id INT NULL,
    object_type NVARCHAR(50) NOT NULL,
    component_code NVARCHAR(100) NOT NULL,
    rule_code NVARCHAR(100) NOT NULL,
    base_value FLOAT NOT NULL,
    order_quantity FLOAT NOT NULL,
    size_coefficient FLOAT NULL,
    color_coefficient FLOAT NULL,
    loss_rate FLOAT NULL,
    quantity_net FLOAT NOT NULL,
    quantity_gross FLOAT NOT NULL,
    unit NVARCHAR(20) NOT NULL,
    formula NVARCHAR(255) NOT NULL,
    created_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    FOREIGN KEY (cbn_run_id) REFERENCES cbn_runs(id),
    FOREIGN KEY (sales_order_line_id) REFERENCES sales_order_lines(id)
);

CREATE TABLE pegging_runs (
    id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    cbn_run_id INT NOT NULL,
    version_no INT NOT NULL DEFAULT 1,
    status NVARCHAR(20) NOT NULL DEFAULT 'COMPLETED' CHECK (status IN ('DRAFT', 'RUNNING', 'COMPLETED', 'ERROR')),
    created_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    notes NVARCHAR(MAX) NULL,
    FOREIGN KEY (cbn_run_id) REFERENCES cbn_runs(id)
);

CREATE TABLE pegging_links (
    id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    pegging_run_id INT NOT NULL,
    link_type NVARCHAR(30) NOT NULL CHECK (link_type IN ('OV_TO_NEED', 'NEED_TO_OF', 'NEED_TO_OA', 'NEED_TO_STOCK', 'OF_TO_NEED', 'OA_TO_NEED', 'STOCK_TO_NEED')),
    direction NVARCHAR(20) NOT NULL CHECK (direction IN ('DOWNSTREAM', 'UPSTREAM')),
    source_entity_type NVARCHAR(30) NOT NULL,
    source_entity_id INT NOT NULL,
    target_entity_type NVARCHAR(30) NOT NULL,
    target_entity_id INT NOT NULL,
    material_requirement_id INT NULL,
    quantity FLOAT NOT NULL CHECK (quantity >= 0),
    unit NVARCHAR(20) NOT NULL,
    version_no INT NOT NULL DEFAULT 1,
    status NVARCHAR(20) NOT NULL DEFAULT 'ACTIVE',
    source_path NVARCHAR(255) NULL,
    created_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    FOREIGN KEY (pegging_run_id) REFERENCES pegging_runs(id),
    FOREIGN KEY (material_requirement_id) REFERENCES cbn_material_requirements(id)
);

CREATE TABLE pegging_link_versions (
    id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    pegging_link_id INT NOT NULL,
    version_no INT NOT NULL,
    quantity FLOAT NOT NULL,
    change_reason NVARCHAR(255) NULL,
    changed_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    UNIQUE (pegging_link_id, version_no),
    FOREIGN KEY (pegging_link_id) REFERENCES pegging_links(id)
);

CREATE TABLE application_logs (
    id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    level NVARCHAR(20) NOT NULL CHECK (level IN ('DEBUG', 'INFO', 'WARNING', 'ERROR')),
    category NVARCHAR(100) NOT NULL,
    message NVARCHAR(MAX) NOT NULL,
    details NVARCHAR(MAX) NULL,
    created_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
);
GO
