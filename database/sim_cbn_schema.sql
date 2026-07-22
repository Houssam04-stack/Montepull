-- Module Simulation CBN/MRP — tables isolees (SimulationId)
-- Ne melange pas les donnees simulees avec le CBN/pegging production.

USE AxioplanMvp;
GO

IF OBJECT_ID(N'dbo.sim_cbn_alerts', N'U') IS NOT NULL DROP TABLE dbo.sim_cbn_alerts;
IF OBJECT_ID(N'dbo.sim_pegging', N'U') IS NOT NULL DROP TABLE dbo.sim_pegging;
IF OBJECT_ID(N'dbo.sim_work_orders', N'U') IS NOT NULL DROP TABLE dbo.sim_work_orders;
IF OBJECT_ID(N'dbo.sim_planned_orders', N'U') IS NOT NULL DROP TABLE dbo.sim_planned_orders;
IF OBJECT_ID(N'dbo.sim_net_requirements', N'U') IS NOT NULL DROP TABLE dbo.sim_net_requirements;
IF OBJECT_ID(N'dbo.sim_gross_requirements', N'U') IS NOT NULL DROP TABLE dbo.sim_gross_requirements;
IF OBJECT_ID(N'dbo.sim_cbn_parameters', N'U') IS NOT NULL DROP TABLE dbo.sim_cbn_parameters;
IF OBJECT_ID(N'dbo.sim_component_substitution_rules', N'U') IS NOT NULL DROP TABLE dbo.sim_component_substitution_rules;
IF OBJECT_ID(N'dbo.sim_duplication_options', N'U') IS NOT NULL DROP TABLE dbo.sim_duplication_options;
IF OBJECT_ID(N'dbo.sim_variant_definitions', N'U') IS NOT NULL DROP TABLE dbo.sim_variant_definitions;
IF OBJECT_ID(N'dbo.sim_routing_operations', N'U') IS NOT NULL DROP TABLE dbo.sim_routing_operations;
IF OBJECT_ID(N'dbo.sim_bom_lines', N'U') IS NOT NULL DROP TABLE dbo.sim_bom_lines;
IF OBJECT_ID(N'dbo.sim_sales_orders', N'U') IS NOT NULL DROP TABLE dbo.sim_sales_orders;
IF OBJECT_ID(N'dbo.sim_articles', N'U') IS NOT NULL DROP TABLE dbo.sim_articles;
IF OBJECT_ID(N'dbo.simulations', N'U') IS NOT NULL DROP TABLE dbo.simulations;
GO

CREATE TABLE simulations (
    id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    name NVARCHAR(200) NOT NULL,
    description NVARCHAR(1000) NULL,
    created_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    created_by NVARCHAR(100) NOT NULL DEFAULT N'system',
    status NVARCHAR(30) NOT NULL DEFAULT N'DRAFT',
    is_active BIT NOT NULL DEFAULT 1
);

CREATE TABLE sim_articles (
    id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    simulation_id INT NOT NULL,
    base_article_id INT NULL,
    code NVARCHAR(100) NOT NULL,
    designation NVARCHAR(255) NOT NULL,
    article_type NVARCHAR(40) NOT NULL,
    size NVARCHAR(50) NULL,
    color NVARCHAR(50) NULL,
    is_template BIT NOT NULL DEFAULT 0,
    is_generated BIT NOT NULL DEFAULT 0,
    procurement_type NVARCHAR(30) NOT NULL,
    lead_time_days INT NOT NULL DEFAULT 0,
    unit NVARCHAR(20) NOT NULL DEFAULT N'PCS',
    is_simulated BIT NOT NULL DEFAULT 1,
    llc INT NULL,
    CONSTRAINT uq_sim_articles_sim_code UNIQUE (simulation_id, code),
    FOREIGN KEY (simulation_id) REFERENCES simulations(id)
);

CREATE TABLE sim_sales_orders (
    id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    simulation_id INT NOT NULL,
    customer_name NVARCHAR(200) NOT NULL,
    customer_order_number NVARCHAR(100) NOT NULL,
    article_id INT NOT NULL,
    quantity FLOAT NOT NULL,
    requested_date DATE NOT NULL,
    priority INT NOT NULL DEFAULT 50,
    created_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    FOREIGN KEY (simulation_id) REFERENCES simulations(id),
    FOREIGN KEY (article_id) REFERENCES sim_articles(id)
);

CREATE TABLE sim_bom_lines (
    id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    simulation_id INT NOT NULL,
    parent_article_id INT NOT NULL,
    component_article_id INT NOT NULL,
    quantity_per FLOAT NOT NULL,
    scrap_rate FLOAT NOT NULL DEFAULT 0,
    offset_days INT NOT NULL DEFAULT 0,
    apply_size_coefficient BIT NOT NULL DEFAULT 0,
    apply_color_substitution BIT NOT NULL DEFAULT 0,
    level INT NULL,
    cumulative_quantity FLOAT NULL,
    is_generated BIT NOT NULL DEFAULT 0,
    is_simulated BIT NOT NULL DEFAULT 1,
    FOREIGN KEY (simulation_id) REFERENCES simulations(id),
    FOREIGN KEY (parent_article_id) REFERENCES sim_articles(id),
    FOREIGN KEY (component_article_id) REFERENCES sim_articles(id)
);

CREATE TABLE sim_routing_operations (
    id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    simulation_id INT NOT NULL,
    article_id INT NOT NULL,
    operation_number INT NOT NULL,
    operation_name NVARCHAR(200) NOT NULL,
    work_center NVARCHAR(100) NULL,
    setup_time_minutes FLOAT NOT NULL DEFAULT 0,
    run_time_minutes FLOAT NOT NULL DEFAULT 0,
    queue_time_minutes FLOAT NOT NULL DEFAULT 0,
    move_time_minutes FLOAT NOT NULL DEFAULT 0,
    apply_size_coefficient BIT NOT NULL DEFAULT 0,
    apply_color_coefficient BIT NOT NULL DEFAULT 0,
    is_generated BIT NOT NULL DEFAULT 0,
    is_simulated BIT NOT NULL DEFAULT 1,
    FOREIGN KEY (simulation_id) REFERENCES simulations(id),
    FOREIGN KEY (article_id) REFERENCES sim_articles(id)
);

CREATE TABLE sim_variant_definitions (
    id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    simulation_id INT NOT NULL,
    template_article_id INT NOT NULL,
    size NVARCHAR(50) NOT NULL,
    color NVARCHAR(50) NOT NULL,
    size_coefficient FLOAT NOT NULL DEFAULT 1,
    color_coefficient FLOAT NOT NULL DEFAULT 1,
    generated_article_code NVARCHAR(100) NOT NULL,
    FOREIGN KEY (simulation_id) REFERENCES simulations(id),
    FOREIGN KEY (template_article_id) REFERENCES sim_articles(id)
);

CREATE TABLE sim_duplication_options (
    id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    simulation_id INT NOT NULL,
    option_type NVARCHAR(10) NOT NULL,
    value NVARCHAR(50) NOT NULL,
    coefficient FLOAT NOT NULL DEFAULT 1,
    is_selected BIT NOT NULL DEFAULT 1,
    sort_order INT NOT NULL DEFAULT 0,
    CONSTRAINT uq_sim_duplication_options UNIQUE (simulation_id, option_type, value),
    FOREIGN KEY (simulation_id) REFERENCES simulations(id)
);

CREATE TABLE sim_component_substitution_rules (
    id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    simulation_id INT NOT NULL,
    base_component_id INT NOT NULL,
    attribute_name NVARCHAR(50) NOT NULL,
    attribute_value NVARCHAR(100) NOT NULL,
    substitute_component_id INT NOT NULL,
    FOREIGN KEY (simulation_id) REFERENCES simulations(id),
    FOREIGN KEY (base_component_id) REFERENCES sim_articles(id),
    FOREIGN KEY (substitute_component_id) REFERENCES sim_articles(id)
);

CREATE TABLE sim_cbn_parameters (
    id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    simulation_id INT NOT NULL,
    article_id INT NOT NULL,
    on_hand_stock FLOAT NOT NULL DEFAULT 0,
    safety_stock FLOAT NOT NULL DEFAULT 0,
    reserved_quantity FLOAT NOT NULL DEFAULT 0,
    scheduled_receipt_production FLOAT NOT NULL DEFAULT 0,
    scheduled_receipt_purchase FLOAT NOT NULL DEFAULT 0,
    lead_time_days INT NOT NULL DEFAULT 0,
    lot_rule NVARCHAR(40) NOT NULL DEFAULT N'LotForLot',
    min_lot FLOAT NOT NULL DEFAULT 0,
    multiple_lot FLOAT NOT NULL DEFAULT 1,
    is_simulated BIT NOT NULL DEFAULT 1,
    CONSTRAINT uq_sim_cbn_parameters UNIQUE (simulation_id, article_id),
    FOREIGN KEY (simulation_id) REFERENCES simulations(id),
    FOREIGN KEY (article_id) REFERENCES sim_articles(id)
);

CREATE TABLE sim_gross_requirements (
    id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    simulation_id INT NOT NULL,
    sales_order_id INT NOT NULL,
    article_id INT NOT NULL,
    parent_article_id INT NULL,
    level INT NOT NULL,
    gross_quantity FLOAT NOT NULL,
    need_date DATE NOT NULL,
    source_type NVARCHAR(50) NOT NULL,
    source_id INT NULL,
    path NVARCHAR(500) NULL,
    FOREIGN KEY (simulation_id) REFERENCES simulations(id),
    FOREIGN KEY (sales_order_id) REFERENCES sim_sales_orders(id),
    FOREIGN KEY (article_id) REFERENCES sim_articles(id)
);

CREATE TABLE sim_net_requirements (
    id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    simulation_id INT NOT NULL,
    sales_order_id INT NOT NULL,
    article_id INT NOT NULL,
    gross_requirement FLOAT NOT NULL,
    on_hand_stock FLOAT NOT NULL,
    safety_stock FLOAT NOT NULL,
    reserved_quantity FLOAT NOT NULL,
    scheduled_receipt_production FLOAT NOT NULL,
    scheduled_receipt_purchase FLOAT NOT NULL,
    net_requirement FLOAT NOT NULL,
    need_date DATE NOT NULL,
    FOREIGN KEY (simulation_id) REFERENCES simulations(id),
    FOREIGN KEY (sales_order_id) REFERENCES sim_sales_orders(id),
    FOREIGN KEY (article_id) REFERENCES sim_articles(id)
);

CREATE TABLE sim_planned_orders (
    id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    simulation_id INT NOT NULL,
    sales_order_id INT NOT NULL,
    article_id INT NOT NULL,
    order_type NVARCHAR(10) NOT NULL,
    quantity FLOAT NOT NULL,
    need_date DATE NOT NULL,
    release_date DATE NOT NULL,
    receipt_date DATE NOT NULL,
    status NVARCHAR(30) NOT NULL DEFAULT N'PROPOSED',
    source_type NVARCHAR(50) NOT NULL,
    source_id INT NULL,
    FOREIGN KEY (simulation_id) REFERENCES simulations(id),
    FOREIGN KEY (sales_order_id) REFERENCES sim_sales_orders(id),
    FOREIGN KEY (article_id) REFERENCES sim_articles(id)
);

CREATE TABLE sim_work_orders (
    id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    simulation_id INT NOT NULL,
    planned_order_id INT NOT NULL,
    sales_order_id INT NOT NULL,
    article_id INT NOT NULL,
    quantity FLOAT NOT NULL,
    start_date DATE NOT NULL,
    end_date DATE NOT NULL,
    status NVARCHAR(30) NOT NULL DEFAULT N'PLANNED',
    priority INT NOT NULL DEFAULT 50,
    routing_reference_id INT NULL,
    bom_reference_id INT NULL,
    FOREIGN KEY (simulation_id) REFERENCES simulations(id),
    FOREIGN KEY (planned_order_id) REFERENCES sim_planned_orders(id),
    FOREIGN KEY (sales_order_id) REFERENCES sim_sales_orders(id),
    FOREIGN KEY (article_id) REFERENCES sim_articles(id)
);

CREATE TABLE sim_pegging (
    id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    simulation_id INT NOT NULL,
    sales_order_id INT NOT NULL,
    planned_order_id INT NULL,
    work_order_id INT NULL,
    parent_article_id INT NULL,
    component_article_id INT NOT NULL,
    level INT NOT NULL,
    quantity FLOAT NOT NULL,
    need_date DATE NOT NULL,
    source_type NVARCHAR(50) NOT NULL,
    source_id INT NULL,
    path NVARCHAR(1000) NOT NULL,
    FOREIGN KEY (simulation_id) REFERENCES simulations(id),
    FOREIGN KEY (sales_order_id) REFERENCES sim_sales_orders(id),
    FOREIGN KEY (component_article_id) REFERENCES sim_articles(id)
);

CREATE TABLE sim_cbn_alerts (
    id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    simulation_id INT NOT NULL,
    article_id INT NULL,
    alert_type NVARCHAR(50) NOT NULL,
    message NVARCHAR(1000) NOT NULL,
    severity NVARCHAR(20) NOT NULL DEFAULT N'WARNING',
    created_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    FOREIGN KEY (simulation_id) REFERENCES simulations(id)
);
GO
