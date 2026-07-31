using Axioplan.GammesNomenclatures.Application.Abstractions;
using Axioplan.GammesNomenclatures.Application.Models;
using Axioplan.GammesNomenclatures.Application.Simulation;
using Axioplan.GammesNomenclatures.Application.SimulationCbn;
using Axioplan.GammesNomenclatures.Domain.Simulation;
using Axioplan.GammesNomenclatures.Infrastructure.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace Axioplan.GammesNomenclatures.Infrastructure.Repositories;

/// <summary>
/// Persistance SQL Server du module Simulation CBN/MRP.
/// Toutes les tables sont prefixees sim_ / simulations et filtrees par SimulationId.
/// </summary>
public sealed class SqlServerSimulationCbnRepository(IOptions<DatabaseOptions> options) : ISimulationCbnRepository
{
    public async Task EnsureSchemaAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            IF OBJECT_ID(N'dbo.simulations', N'U') IS NULL
            BEGIN
                CREATE TABLE simulations (
                    id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    name NVARCHAR(200) NOT NULL,
                    description NVARCHAR(1000) NULL,
                    created_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
                    created_by NVARCHAR(100) NOT NULL DEFAULT N'system',
                    status NVARCHAR(30) NOT NULL DEFAULT N'DRAFT',
                    is_active BIT NOT NULL DEFAULT 1
                );
            END;

            IF OBJECT_ID(N'dbo.sim_articles', N'U') IS NULL
            BEGIN
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
            END;

            IF OBJECT_ID(N'dbo.sim_sales_orders', N'U') IS NULL
            BEGIN
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
            END;

            IF OBJECT_ID(N'dbo.sim_bom_lines', N'U') IS NULL
            BEGIN
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
                    nomenclature_type NVARCHAR(20) NOT NULL DEFAULT N'BASE',
                    alternative INT NOT NULL DEFAULT 0,
                    FOREIGN KEY (simulation_id) REFERENCES simulations(id),
                    FOREIGN KEY (parent_article_id) REFERENCES sim_articles(id),
                    FOREIGN KEY (component_article_id) REFERENCES sim_articles(id)
                );
            END;

            IF COL_LENGTH(N'dbo.sim_bom_lines', N'nomenclature_type') IS NULL
            BEGIN
                ALTER TABLE sim_bom_lines ADD nomenclature_type NVARCHAR(20) NOT NULL CONSTRAINT DF_sim_bom_lines_nomenclature_type DEFAULT N'BASE';
            END;

            IF COL_LENGTH(N'dbo.sim_bom_lines', N'alternative') IS NULL
            BEGIN
                ALTER TABLE sim_bom_lines ADD alternative INT NOT NULL CONSTRAINT DF_sim_bom_lines_alternative DEFAULT 0;
            END;

            IF OBJECT_ID(N'dbo.sim_routing_operations', N'U') IS NULL
            BEGIN
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
            END;

            IF OBJECT_ID(N'dbo.sim_variant_definitions', N'U') IS NULL
            BEGIN
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
            END;

            IF OBJECT_ID(N'dbo.sim_duplication_options', N'U') IS NULL
            BEGIN
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
            END;

            IF OBJECT_ID(N'dbo.sim_component_substitution_rules', N'U') IS NULL
            BEGIN
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
            END;

            IF OBJECT_ID(N'dbo.sim_cbn_parameters', N'U') IS NULL
            BEGIN
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
            END;

            IF OBJECT_ID(N'dbo.sim_gross_requirements', N'U') IS NULL
            BEGIN
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
                    FOREIGN KEY (simulation_id) REFERENCES simulations(id)
                );
            END;

            IF OBJECT_ID(N'dbo.sim_net_requirements', N'U') IS NULL
            BEGIN
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
                    FOREIGN KEY (simulation_id) REFERENCES simulations(id)
                );
            END;

            IF OBJECT_ID(N'dbo.sim_planned_orders', N'U') IS NULL
            BEGIN
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
                    FOREIGN KEY (simulation_id) REFERENCES simulations(id)
                );
            END;

            IF OBJECT_ID(N'dbo.sim_work_orders', N'U') IS NULL
            BEGIN
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
                    FOREIGN KEY (simulation_id) REFERENCES simulations(id)
                );
            END;

            IF OBJECT_ID(N'dbo.sim_pegging', N'U') IS NULL
            BEGIN
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
                    FOREIGN KEY (simulation_id) REFERENCES simulations(id)
                );
            END;

            IF OBJECT_ID(N'dbo.sim_cbn_alerts', N'U') IS NULL
            BEGIN
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
            END;

            IF OBJECT_ID(N'dbo.sim_cbn_runs', N'U') IS NULL
            BEGIN
                CREATE TABLE sim_cbn_runs (
                    id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    simulation_id INT NOT NULL,
                    sales_order_id INT NOT NULL,
                    article_id INT NOT NULL,
                    quantity FLOAT NOT NULL,
                    requested_date DATE NOT NULL,
                    customer_name NVARCHAR(200) NOT NULL,
                    customer_order_number NVARCHAR(100) NOT NULL,
                    run_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
                    planned_order_count INT NOT NULL DEFAULT 0,
                    alert_count INT NOT NULL DEFAULT 0,
                    net_line_count INT NOT NULL DEFAULT 0,
                    FOREIGN KEY (simulation_id) REFERENCES simulations(id)
                );
            END;

            IF OBJECT_ID(N'dbo.sim_flat_bom_lines', N'U') IS NULL
            BEGIN
                CREATE TABLE sim_flat_bom_lines (
                    id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    cbn_run_id INT NOT NULL,
                    simulation_id INT NOT NULL,
                    level INT NOT NULL,
                    parent_article_code NVARCHAR(100) NULL,
                    component_article_code NVARCHAR(100) NOT NULL,
                    quantity_per FLOAT NOT NULL,
                    cumulative_quantity FLOAT NOT NULL,
                    need_date DATE NOT NULL,
                    path NVARCHAR(1000) NOT NULL,
                    FOREIGN KEY (cbn_run_id) REFERENCES sim_cbn_runs(id),
                    FOREIGN KEY (simulation_id) REFERENCES simulations(id)
                );
            END;

            IF COL_LENGTH('dbo.sim_gross_requirements', 'cbn_run_id') IS NULL
                ALTER TABLE sim_gross_requirements ADD cbn_run_id INT NULL;
            IF COL_LENGTH('dbo.sim_net_requirements', 'cbn_run_id') IS NULL
                ALTER TABLE sim_net_requirements ADD cbn_run_id INT NULL;
            IF COL_LENGTH('dbo.sim_planned_orders', 'cbn_run_id') IS NULL
                ALTER TABLE sim_planned_orders ADD cbn_run_id INT NULL;
            IF COL_LENGTH('dbo.sim_work_orders', 'cbn_run_id') IS NULL
                ALTER TABLE sim_work_orders ADD cbn_run_id INT NULL;
            IF COL_LENGTH('dbo.sim_pegging', 'cbn_run_id') IS NULL
                ALTER TABLE sim_pegging ADD cbn_run_id INT NULL;
            IF COL_LENGTH('dbo.sim_cbn_alerts', 'cbn_run_id') IS NULL
                ALTER TABLE sim_cbn_alerts ADD cbn_run_id INT NULL;
            IF COL_LENGTH('dbo.sim_planned_orders', 'justification') IS NULL
                ALTER TABLE sim_planned_orders ADD justification NVARCHAR(1000) NULL;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SimulationHeaderDto>> ListSimulationsAsync(CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);
        await using var connection = OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, name, description, created_at, created_by, status, is_active
            FROM simulations
            ORDER BY created_at DESC
            """;
        var list = new List<SimulationHeaderDto>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new SimulationHeaderDto(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.GetDateTime(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetBoolean(6)));
        }

        return list;
    }

    public async Task<SimulationHeaderDto> CreateSimulationAsync(CreateSimulationRequest request, CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);
        await using var connection = OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO simulations (name, description, created_by, status, is_active)
            OUTPUT INSERTED.id, INSERTED.name, INSERTED.description, INSERTED.created_at, INSERTED.created_by, INSERTED.status, INSERTED.is_active
            VALUES (@name, @description, @createdBy, 'DRAFT', 1)
            """;
        command.Parameters.AddWithValue("@name", request.Name.Trim());
        command.Parameters.AddWithValue("@description", (object?)request.Description ?? DBNull.Value);
        command.Parameters.AddWithValue("@createdBy", string.IsNullOrWhiteSpace(request.CreatedBy) ? "user" : request.CreatedBy);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return new SimulationHeaderDto(
            reader.GetInt32(0),
            reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.GetDateTime(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetBoolean(6));
    }

    public async Task DeleteSimulationAsync(int simulationId, CancellationToken cancellationToken = default)
    {
        if (simulationId <= 0)
        {
            throw new ArgumentException("Identifiant de simulation invalide.", nameof(simulationId));
        }

        await EnsureSchemaAsync(cancellationToken);
        await using var connection = OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            DELETE FROM sim_cbn_alerts WHERE simulation_id = @id;
            DELETE FROM sim_pegging WHERE simulation_id = @id;
            DELETE FROM sim_work_orders WHERE simulation_id = @id;
            DELETE FROM sim_planned_orders WHERE simulation_id = @id;
            DELETE FROM sim_net_requirements WHERE simulation_id = @id;
            DELETE FROM sim_gross_requirements WHERE simulation_id = @id;
            DELETE FROM sim_sales_orders WHERE simulation_id = @id;
            DELETE FROM sim_variant_definitions WHERE simulation_id = @id;
            DELETE FROM sim_component_substitution_rules WHERE simulation_id = @id;
            DELETE FROM sim_bom_lines WHERE simulation_id = @id;
            DELETE FROM sim_routing_operations WHERE simulation_id = @id;
            DELETE FROM sim_cbn_parameters WHERE simulation_id = @id;
            DELETE FROM sim_duplication_options WHERE simulation_id = @id;
            DELETE FROM sim_articles WHERE simulation_id = @id;
            DELETE FROM simulations WHERE id = @id;
            """;
        command.Parameters.AddWithValue("@id", simulationId);
        var deleted = await command.ExecuteNonQueryAsync(cancellationToken);
        if (deleted == 0)
        {
            throw new InvalidOperationException($"Simulation #{simulationId} introuvable.");
        }
    }

    public async Task<SimulationHeaderDto?> GetSimulationAsync(int simulationId, CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);
        await using var connection = OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, name, description, created_at, created_by, status, is_active
            FROM simulations WHERE id = @id
            """;
        command.Parameters.AddWithValue("@id", simulationId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new SimulationHeaderDto(
            reader.GetInt32(0),
            reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.GetDateTime(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetBoolean(6));
    }

    public async Task ClearCbnResultsAsync(int simulationId, CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);
        await using var connection = OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            DELETE FROM sim_cbn_alerts WHERE simulation_id = @id;
            DELETE FROM sim_pegging WHERE simulation_id = @id;
            DELETE FROM sim_work_orders WHERE simulation_id = @id;
            DELETE FROM sim_planned_orders WHERE simulation_id = @id;
            DELETE FROM sim_net_requirements WHERE simulation_id = @id;
            DELETE FROM sim_gross_requirements WHERE simulation_id = @id;
            DELETE FROM sim_flat_bom_lines WHERE simulation_id = @id;
            DELETE FROM sim_cbn_runs WHERE simulation_id = @id;
            """;
        command.Parameters.AddWithValue("@id", simulationId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SeedPantalonDemoAsync(int simulationId, CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);
        await using var connection = OpenConnection();

        await using (var check = connection.CreateCommand())
        {
            check.CommandText = "SELECT COUNT(*) FROM sim_articles WHERE simulation_id = @id AND code = 'PANTALON_BASE'";
            check.Parameters.AddWithValue("@id", simulationId);
            if (Convert.ToInt32(await check.ExecuteScalarAsync(cancellationToken)) > 0)
            {
                await EnsureDefaultDuplicationOptionsAsync(simulationId, cancellationToken);
                return;
            }
        }

        async Task<int> InsertArticle(
            string code,
            string designation,
            string type,
            string procurement,
            int leadTime,
            bool isTemplate = false,
            string? size = null,
            string? color = null,
            bool isGenerated = false)
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                INSERT INTO sim_articles (simulation_id, code, designation, article_type, size, color, is_template, is_generated, procurement_type, lead_time_days, unit, is_simulated)
                OUTPUT INSERTED.id
                VALUES (@sim, @code, @des, @type, @size, @color, @template, @generated, @proc, @lead, 'PCS', 1)
                """;
            cmd.Parameters.AddWithValue("@sim", simulationId);
            cmd.Parameters.AddWithValue("@code", code);
            cmd.Parameters.AddWithValue("@des", designation);
            cmd.Parameters.AddWithValue("@type", type);
            cmd.Parameters.AddWithValue("@size", (object?)size ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@color", (object?)color ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@template", isTemplate);
            cmd.Parameters.AddWithValue("@generated", isGenerated);
            cmd.Parameters.AddWithValue("@proc", procurement);
            cmd.Parameters.AddWithValue("@lead", leadTime);
            return (int)(await cmd.ExecuteScalarAsync(cancellationToken) ?? throw new InvalidOperationException());
        }

        var baseId = await InsertArticle("PANTALON_BASE", "Pantalon base", SimulationArticleTypes.FinishedGood, SimulationProcurementTypes.Manufactured, 5, isTemplate: true);
        var tissuBase = await InsertArticle("TISSU_BASE", "Tissu base", SimulationArticleTypes.RawMaterial, SimulationProcurementTypes.Purchased, 4);
        var tissuNoir = await InsertArticle("TISSU_NOIR", "Tissu noir", SimulationArticleTypes.RawMaterial, SimulationProcurementTypes.Purchased, 4);
        var tissuBleu = await InsertArticle("TISSU_BLEU", "Tissu bleu", SimulationArticleTypes.RawMaterial, SimulationProcurementTypes.Purchased, 4);
        var fil = await InsertArticle("FIL", "Fil", SimulationArticleTypes.Purchased, SimulationProcurementTypes.Purchased, 2);
        var bouton = await InsertArticle("BOUTON", "Bouton", SimulationArticleTypes.Purchased, SimulationProcurementTypes.Purchased, 2);
        var zip = await InsertArticle("ZIP", "Zip", SimulationArticleTypes.Purchased, SimulationProcurementTypes.Purchased, 3);
        var emballage = await InsertArticle("EMBALLAGE", "Emballage", SimulationArticleTypes.Purchased, SimulationProcurementTypes.Purchased, 1);

        async Task InsertBom(int parent, int component, double qty, bool sizeCoef, bool colorSub)
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                INSERT INTO sim_bom_lines (simulation_id, parent_article_id, component_article_id, quantity_per, scrap_rate, offset_days, apply_size_coefficient, apply_color_substitution, is_generated, is_simulated, nomenclature_type, alternative)
                VALUES (@sim, @parent, @comp, @qty, 0, 0, @size, @color, 0, 1, @nomType, 0)
                """;
            cmd.Parameters.AddWithValue("@sim", simulationId);
            cmd.Parameters.AddWithValue("@parent", parent);
            cmd.Parameters.AddWithValue("@comp", component);
            cmd.Parameters.AddWithValue("@qty", qty);
            cmd.Parameters.AddWithValue("@size", sizeCoef);
            cmd.Parameters.AddWithValue("@color", colorSub);
            cmd.Parameters.AddWithValue("@nomType", SimulationNomenclatureTypes.Base);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await InsertBom(baseId, tissuBase, 1.20, true, true);
        await InsertBom(baseId, fil, 0.05, true, false);
        await InsertBom(baseId, bouton, 1, false, false);
        await InsertBom(baseId, zip, 1, false, false);
        await InsertBom(baseId, emballage, 1, false, false);

        async Task InsertOp(int articleId, int no, string name, double run, bool sizeCoef)
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                INSERT INTO sim_routing_operations (simulation_id, article_id, operation_number, operation_name, work_center, setup_time_minutes, run_time_minutes, queue_time_minutes, move_time_minutes, apply_size_coefficient, apply_color_coefficient, is_generated, is_simulated)
                VALUES (@sim, @art, @no, @name, @wc, 0, @run, 0, 0, @size, 0, 0, 1)
                """;
            cmd.Parameters.AddWithValue("@sim", simulationId);
            cmd.Parameters.AddWithValue("@art", articleId);
            cmd.Parameters.AddWithValue("@no", no);
            cmd.Parameters.AddWithValue("@name", name);
            cmd.Parameters.AddWithValue("@wc", "ATELIER");
            cmd.Parameters.AddWithValue("@run", run);
            cmd.Parameters.AddWithValue("@size", sizeCoef);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await InsertOp(baseId, 10, "Coupe tissu", 8, true);
        await InsertOp(baseId, 20, "Couture", 20, true);
        await InsertOp(baseId, 30, "Montage zip", 5, false);
        await InsertOp(baseId, 40, "Controle qualite", 3, false);
        await InsertOp(baseId, 50, "Emballage", 2, false);

        async Task InsertSub(int baseComp, string value, int substitute)
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                INSERT INTO sim_component_substitution_rules (simulation_id, base_component_id, attribute_name, attribute_value, substitute_component_id)
                VALUES (@sim, @base, 'COLOR', @val, @sub)
                """;
            cmd.Parameters.AddWithValue("@sim", simulationId);
            cmd.Parameters.AddWithValue("@base", baseComp);
            cmd.Parameters.AddWithValue("@val", value);
            cmd.Parameters.AddWithValue("@sub", substitute);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await InsertSub(tissuBase, "Noir", tissuNoir);
        await InsertSub(tissuBase, "Bleu", tissuBleu);

        async Task InsertParam(int articleId, double stock, double safety, double reserved, double mfg, double purch, int lead)
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                INSERT INTO sim_cbn_parameters (simulation_id, article_id, on_hand_stock, safety_stock, reserved_quantity, scheduled_receipt_production, scheduled_receipt_purchase, lead_time_days, lot_rule, min_lot, multiple_lot, is_simulated)
                VALUES (@sim, @art, @stock, @safety, @reserved, @mfg, @purch, @lead, 'LotForLot', 0, 1, 1)
                """;
            cmd.Parameters.AddWithValue("@sim", simulationId);
            cmd.Parameters.AddWithValue("@art", articleId);
            cmd.Parameters.AddWithValue("@stock", stock);
            cmd.Parameters.AddWithValue("@safety", safety);
            cmd.Parameters.AddWithValue("@reserved", reserved);
            cmd.Parameters.AddWithValue("@mfg", mfg);
            cmd.Parameters.AddWithValue("@purch", purch);
            cmd.Parameters.AddWithValue("@lead", lead);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await InsertParam(baseId, 0, 0, 0, 0, 0, 5);
        await InsertParam(tissuBase, 0, 0, 0, 0, 0, 4);
        await InsertParam(tissuNoir, 50, 10, 0, 0, 20, 4);
        await InsertParam(tissuBleu, 30, 10, 0, 0, 0, 4);
        await InsertParam(fil, 2, 1, 0, 0, 0, 2);
        await InsertParam(bouton, 200, 50, 0, 0, 0, 2);
        await InsertParam(zip, 70, 20, 0, 0, 0, 3);
        await InsertParam(emballage, 100, 20, 0, 0, 0, 1);

        await EnsureDefaultDuplicationOptionsAsync(simulationId, cancellationToken);
    }

    public async Task<SimulationHeaderDto> EnsureTunimapulfSimulationAsync(CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);

        var existing = (await ListSimulationsAsync(cancellationToken))
            .FirstOrDefault(s => string.Equals(s.Name, SimulationArticleNaming.TunimapulfSimulationName, StringComparison.OrdinalIgnoreCase));

        var simulation = existing ?? await CreateSimulationAsync(
            new CreateSimulationRequest(
                SimulationArticleNaming.TunimapulfSimulationName,
                "Simulation Montepull — duplication taille/couleur + CBN (stocks synchronises)"),
            cancellationToken);

        await SeedTunimapulfDemoAsync(simulation.Id, cancellationToken);
        await SyncStocksIntoSimCbnParametersAsync(simulation.Id, cancellationToken);
        await EnsureTunimapulfVariantsAsync(simulation.Id, cancellationToken);
        return (await GetSimulationAsync(simulation.Id, cancellationToken))!;
    }

    private async Task EnsureTunimapulfVariantsAsync(int simulationId, CancellationToken cancellationToken)
    {
        var articles = await GetArticlesAsync(simulationId, cancellationToken);
        var template = articles.FirstOrDefault(a => a.Code == "TUNIMAPULF_BASE" && a.IsTemplate);
        if (template is null)
        {
            return;
        }

        if (articles.Any(a => a.IsGenerated && a.BaseArticleId == template.Id))
        {
            return;
        }

        var options = await GetDuplicationOptionsAsync(simulationId, cancellationToken);
        var sizes = options.Sizes.Where(s => s.IsSelected).Select(s => new SizeCoefficientInput(s.Size, s.Coefficient)).ToList();
        var colors = options.Colors.Where(c => c.IsSelected).Select(c => new ColorCoefficientInput(c.Color, c.Coefficient)).ToList();
        if (sizes.Count == 0 || colors.Count == 0)
        {
            sizes = DuplicationOptionsDefaults.Sizes.Select(s => new SizeCoefficientInput(s.Size, s.Coefficient)).ToList();
            colors = DuplicationOptionsDefaults.Colors.Select(c => new ColorCoefficientInput(c.Color, c.Coefficient)).ToList();
        }

        await GenerateVariantsAsync(
            new GenerateVariantsRequest(simulationId, template.Id, sizes, colors),
            cancellationToken);
        await SyncStocksIntoSimCbnParametersAsync(simulationId, cancellationToken);
    }

    public async Task SyncStocksIntoSimCbnParametersAsync(int simulationId, CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);
        await using var connection = OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE p
            SET on_hand_stock = sb.quantity_available
            FROM sim_cbn_parameters p
            INNER JOIN sim_articles sa ON sa.id = p.article_id AND sa.simulation_id = p.simulation_id
            INNER JOIN articles a ON a.code = sa.code
            INNER JOIN stock_balances sb ON sb.article_id = a.id
            WHERE p.simulation_id = @sim
            """;
        command.Parameters.AddWithValue("@sim", simulationId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Seed TUNIMAPULF_BASE + composants codes stocks Montepull + BOM/gamme + options duplication.
    /// </summary>
    private async Task SeedTunimapulfDemoAsync(int simulationId, CancellationToken cancellationToken)
    {
        await using var connection = OpenConnection();

        await using (var check = connection.CreateCommand())
        {
            check.CommandText = "SELECT COUNT(*) FROM sim_articles WHERE simulation_id = @id AND code = 'TUNIMAPULF_BASE'";
            check.Parameters.AddWithValue("@id", simulationId);
            if (Convert.ToInt32(await check.ExecuteScalarAsync(cancellationToken)) > 0)
            {
                await EnsureDefaultDuplicationOptionsAsync(simulationId, cancellationToken);
                return;
            }
        }

        async Task<int> InsertArticle(
            string code,
            string designation,
            string type,
            string procurement,
            int leadTime,
            bool isTemplate = false)
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                INSERT INTO sim_articles (simulation_id, code, designation, article_type, size, color, is_template, is_generated, procurement_type, lead_time_days, unit, is_simulated)
                OUTPUT INSERTED.id
                VALUES (@sim, @code, @des, @type, NULL, NULL, @template, 0, @proc, @lead, 'UN', 1)
                """;
            cmd.Parameters.AddWithValue("@sim", simulationId);
            cmd.Parameters.AddWithValue("@code", code);
            cmd.Parameters.AddWithValue("@des", designation);
            cmd.Parameters.AddWithValue("@type", type);
            cmd.Parameters.AddWithValue("@template", isTemplate);
            cmd.Parameters.AddWithValue("@proc", procurement);
            cmd.Parameters.AddWithValue("@lead", leadTime);
            return (int)(await cmd.ExecuteScalarAsync(cancellationToken) ?? throw new InvalidOperationException());
        }

        // Composants : codes presents dans stock_balances (sync CBN apres seed).
        var componentSpecs = new (string Code, string Label, double Qty, bool SizeCoef, int Lead)[]
        {
            ("AC240018", "Composant AC240018", 1.20, true, 4),
            ("EL240112", "Composant EL240112", 0.05, true, 2),
            ("EL240111", "Composant EL240111", 1.00, false, 2),
            ("AV240014", "Composant AV240014", 1.00, false, 3),
        };

        var labels = await LoadArticleLabelsAsync(connection, componentSpecs.Select(c => c.Code).ToArray(), cancellationToken);

        var baseId = await InsertArticle(
            "TUNIMAPULF_BASE",
            "TUNIMAPULF (template)",
            SimulationArticleTypes.FinishedGood,
            SimulationProcurementTypes.Manufactured,
            5,
            isTemplate: true);

        var componentIds = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var (code, fallbackLabel, _, _, lead) in componentSpecs)
        {
            var label = labels.TryGetValue(code, out var fromStock) ? fromStock : fallbackLabel;
            componentIds[code] = await InsertArticle(
                code,
                label,
                SimulationArticleTypes.Purchased,
                SimulationProcurementTypes.Purchased,
                lead);
        }

        async Task InsertBom(int parent, int component, double qty, bool sizeCoef)
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                INSERT INTO sim_bom_lines (simulation_id, parent_article_id, component_article_id, quantity_per, scrap_rate, offset_days, apply_size_coefficient, apply_color_substitution, is_generated, is_simulated, nomenclature_type, alternative)
                VALUES (@sim, @parent, @comp, @qty, 0, 0, @size, 0, 0, 1, @nomType, 0)
                """;
            cmd.Parameters.AddWithValue("@sim", simulationId);
            cmd.Parameters.AddWithValue("@parent", parent);
            cmd.Parameters.AddWithValue("@comp", component);
            cmd.Parameters.AddWithValue("@qty", qty);
            cmd.Parameters.AddWithValue("@size", sizeCoef);
            cmd.Parameters.AddWithValue("@nomType", SimulationNomenclatureTypes.Base);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var (code, _, qty, sizeCoef, _) in componentSpecs)
        {
            await InsertBom(baseId, componentIds[code], qty, sizeCoef);
        }

        async Task InsertOp(int articleId, int no, string name, double run, bool sizeCoef)
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                INSERT INTO sim_routing_operations (simulation_id, article_id, operation_number, operation_name, work_center, setup_time_minutes, run_time_minutes, queue_time_minutes, move_time_minutes, apply_size_coefficient, apply_color_coefficient, is_generated, is_simulated)
                VALUES (@sim, @art, @no, @name, @wc, 0, @run, 0, 0, @size, 0, 0, 1)
                """;
            cmd.Parameters.AddWithValue("@sim", simulationId);
            cmd.Parameters.AddWithValue("@art", articleId);
            cmd.Parameters.AddWithValue("@no", no);
            cmd.Parameters.AddWithValue("@name", name);
            cmd.Parameters.AddWithValue("@wc", "ATELIER");
            cmd.Parameters.AddWithValue("@run", run);
            cmd.Parameters.AddWithValue("@size", sizeCoef);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await InsertOp(baseId, 10, "Preparation", 8, true);
        await InsertOp(baseId, 20, "Assemblage", 20, true);
        await InsertOp(baseId, 30, "Controle", 3, false);
        await InsertOp(baseId, 40, "Emballage", 2, false);

        async Task InsertParam(int articleId, int lead)
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                INSERT INTO sim_cbn_parameters (simulation_id, article_id, on_hand_stock, safety_stock, reserved_quantity, scheduled_receipt_production, scheduled_receipt_purchase, lead_time_days, lot_rule, min_lot, multiple_lot, is_simulated)
                VALUES (@sim, @art, 0, 0, 0, 0, 0, @lead, 'LotForLot', 0, 1, 1)
                """;
            cmd.Parameters.AddWithValue("@sim", simulationId);
            cmd.Parameters.AddWithValue("@art", articleId);
            cmd.Parameters.AddWithValue("@lead", lead);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await InsertParam(baseId, 5);
        foreach (var (code, _, _, _, lead) in componentSpecs)
        {
            await InsertParam(componentIds[code], lead);
        }

        await EnsureDefaultDuplicationOptionsAsync(simulationId, cancellationToken);
    }

    private static async Task<Dictionary<string, string>> LoadArticleLabelsAsync(
        SqlConnection connection,
        IReadOnlyList<string> codes,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (codes.Count == 0)
        {
            return result;
        }

        await using var cmd = connection.CreateCommand();
        var parameters = new List<string>();
        for (var i = 0; i < codes.Count; i++)
        {
            var name = $"@c{i}";
            parameters.Add(name);
            cmd.Parameters.AddWithValue(name, codes[i]);
        }

        cmd.CommandText = $"SELECT code, label FROM articles WHERE code IN ({string.Join(",", parameters)})";
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result[reader.GetString(0)] = reader.IsDBNull(1) ? reader.GetString(0) : reader.GetString(1);
        }

        return result;
    }

    public async Task<DuplicationOptionsDto> GetDuplicationOptionsAsync(
        int simulationId,
        CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);
        await using var connection = OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT option_type, value, coefficient, is_selected, sort_order
            FROM sim_duplication_options
            WHERE simulation_id = @id
            ORDER BY option_type, sort_order, value
            """;
        command.Parameters.AddWithValue("@id", simulationId);

        var sizes = new List<DuplicationSizeOptionDto>();
        var colors = new List<DuplicationColorOptionDto>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var type = reader.GetString(0);
            var value = reader.GetString(1);
            var coefficient = reader.GetDouble(2);
            var isSelected = reader.GetBoolean(3);
            var sortOrder = reader.GetInt32(4);

            if (string.Equals(type, "SIZE", StringComparison.OrdinalIgnoreCase))
            {
                sizes.Add(new DuplicationSizeOptionDto(value, coefficient, isSelected, sortOrder));
            }
            else if (string.Equals(type, "COLOR", StringComparison.OrdinalIgnoreCase))
            {
                colors.Add(new DuplicationColorOptionDto(value, coefficient, isSelected, sortOrder));
            }
        }

        return new DuplicationOptionsDto(sizes, colors);
    }

    public async Task SaveDuplicationOptionsAsync(
        SaveDuplicationOptionsRequest request,
        CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);
        await using var connection = OpenConnection();

        await using (var delete = connection.CreateCommand())
        {
            delete.CommandText = "DELETE FROM sim_duplication_options WHERE simulation_id = @id";
            delete.Parameters.AddWithValue("@id", request.SimulationId);
            await delete.ExecuteNonQueryAsync(cancellationToken);
        }

        async Task InsertAsync(string type, string value, double coefficient, bool isSelected, int sortOrder)
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                INSERT INTO sim_duplication_options (simulation_id, option_type, value, coefficient, is_selected, sort_order)
                VALUES (@sim, @type, @value, @coef, @selected, @sort)
                """;
            cmd.Parameters.AddWithValue("@sim", request.SimulationId);
            cmd.Parameters.AddWithValue("@type", type);
            cmd.Parameters.AddWithValue("@value", value);
            cmd.Parameters.AddWithValue("@coef", coefficient);
            cmd.Parameters.AddWithValue("@selected", isSelected);
            cmd.Parameters.AddWithValue("@sort", sortOrder);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        for (var i = 0; i < request.Sizes.Count; i++)
        {
            var size = request.Sizes[i];
            await InsertAsync("SIZE", size.Size.Trim(), size.Coefficient, size.IsSelected, size.SortOrder >= 0 ? size.SortOrder : i);
        }

        for (var i = 0; i < request.Colors.Count; i++)
        {
            var color = request.Colors[i];
            await InsertAsync("COLOR", color.Color.Trim(), color.Coefficient, color.IsSelected, color.SortOrder >= 0 ? color.SortOrder : i);
        }
    }

    public async Task EnsureDefaultDuplicationOptionsAsync(
        int simulationId,
        CancellationToken cancellationToken = default)
    {
        var existing = await GetDuplicationOptionsAsync(simulationId, cancellationToken);
        if (existing.Sizes.Count > 0 || existing.Colors.Count > 0)
        {
            return;
        }

        await SaveDuplicationOptionsAsync(
            new SaveDuplicationOptionsRequest(
                simulationId,
                DuplicationOptionsDefaults.Sizes,
                DuplicationOptionsDefaults.Colors),
            cancellationToken);
    }

    public async Task<SeedTemplateArticleResult> SeedTemplateArticleAsync(
        SeedTemplateArticleRequest request,
        CancellationToken cancellationToken = default)
    {
        var code = SimulationArticleNaming.NormalizeTemplateCode(request.ArticleName);
        if (SimulationArticleNaming.IsPantalonDemoSeed(request.ArticleName))
        {
            await SeedPantalonDemoAsync(request.SimulationId, cancellationToken);
            var pantalonArticles = await GetArticlesAsync(request.SimulationId, cancellationToken);
            var pantalon = pantalonArticles.First(a => a.Code == "PANTALON_BASE");
            return new SeedTemplateArticleResult(pantalon.Id, pantalon.Code, false);
        }

        if (SimulationArticleNaming.IsTunimapulfSeed(request.ArticleName))
        {
            await SeedTunimapulfDemoAsync(request.SimulationId, cancellationToken);
            await SyncStocksIntoSimCbnParametersAsync(request.SimulationId, cancellationToken);
            var tunimaArticles = await GetArticlesAsync(request.SimulationId, cancellationToken);
            var tunima = tunimaArticles.First(a => a.Code == "TUNIMAPULF_BASE");
            return new SeedTemplateArticleResult(tunima.Id, tunima.Code, false);
        }

        await EnsureSchemaAsync(cancellationToken);
        await using var connection = OpenConnection();

        var existingId = await FindArticleIdByCodeAsync(connection, request.SimulationId, code, cancellationToken);
        if (existingId is int id)
        {
            await EnsureDefaultDuplicationOptionsAsync(request.SimulationId, cancellationToken);
            return new SeedTemplateArticleResult(id, code, true);
        }

        var designation = BuildTemplateDesignation(request.ArticleName, code);
        var articleId = await InsertSimArticleAsync(
            connection,
            request.SimulationId,
            code,
            designation,
            SimulationArticleTypes.FinishedGood,
            SimulationProcurementTypes.Manufactured,
            leadTimeDays: 5,
            isTemplate: true,
            SimulationBomUnits.Un,
            cancellationToken: cancellationToken);

        await InsertDefaultCbnParameterAsync(connection, request.SimulationId, articleId, leadTimeDays: 5, cancellationToken);
        await EnsureDefaultDuplicationOptionsAsync(request.SimulationId, cancellationToken);
        return new SeedTemplateArticleResult(articleId, code, false);
    }

    public async Task<SimulationArticleDto> CreateSimArticleAsync(
        CreateSimArticleRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Code))
        {
            throw new ArgumentException("Le code article est requis.");
        }

        var code = request.Code.Trim().ToUpperInvariant().Replace(' ', '_');
        await EnsureSchemaAsync(cancellationToken);
        await using var connection = OpenConnection();

        if (await FindArticleIdByCodeAsync(connection, request.SimulationId, code, cancellationToken) is not null)
        {
            throw new InvalidOperationException($"L'article {code} existe deja dans cette simulation.");
        }

        var articleId = await InsertSimArticleAsync(
            connection,
            request.SimulationId,
            code,
            request.Designation.Trim(),
            request.ArticleType,
            request.ProcurementType,
            request.LeadTimeDays,
            request.IsTemplate,
            SimulationBomUnits.Normalize(request.Unit),
            cancellationToken: cancellationToken);

        await InsertDefaultCbnParameterAsync(connection, request.SimulationId, articleId, request.LeadTimeDays, cancellationToken);
        return (await GetArticlesAsync(request.SimulationId, cancellationToken))
            .First(a => a.Id == articleId);
    }

    public async Task<SimulationBomLineDto> UpsertSimBomLineAsync(
        UpsertSimBomLineRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.QuantityPer < 0)
        {
            throw new ArgumentException("La quantite par ne peut pas etre negative.");
        }

        var nomenclatureType = SimulationNomenclatureTypes.Normalize(request.NomenclatureType);
        await EnsureSchemaAsync(cancellationToken);
        await using var connection = OpenConnection();
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            await EnsureEditableTemplateParentAsync(connection, request.SimulationId, request.ParentArticleId, cancellationToken, transaction);
            await EnsureArticleBelongsToSimulationAsync(connection, request.SimulationId, request.ComponentArticleId, cancellationToken, transaction);

            if (request.ParentArticleId == request.ComponentArticleId)
            {
                throw new InvalidOperationException("Un article ne peut pas etre composant de lui-meme.");
            }

            var unit = SimulationBomUnits.Normalize(request.Unit);
            var alternative = request.Alternative;
            int bomLineId;

            if (request.BomLineId is int existingBomLineId)
            {
                bomLineId = existingBomLineId;
                await using var update = connection.CreateCommand();
                update.Transaction = transaction;
                update.CommandText = """
                    UPDATE sim_bom_lines
                    SET component_article_id = @comp,
                        quantity_per = @qty,
                        scrap_rate = @scrap,
                        offset_days = @offset,
                        apply_size_coefficient = @size,
                        apply_color_substitution = @color,
                        alternative = @alt
                    WHERE id = @id AND simulation_id = @sim AND parent_article_id = @parent AND is_generated = 0
                    """;
                update.Parameters.AddWithValue("@comp", request.ComponentArticleId);
                update.Parameters.AddWithValue("@qty", request.QuantityPer);
                update.Parameters.AddWithValue("@scrap", request.ScrapRate);
                update.Parameters.AddWithValue("@offset", request.OffsetDays);
                update.Parameters.AddWithValue("@size", request.ApplySizeCoefficient);
                update.Parameters.AddWithValue("@color", request.ApplyColorSubstitution);
                update.Parameters.AddWithValue("@alt", alternative);
                update.Parameters.AddWithValue("@id", bomLineId);
                update.Parameters.AddWithValue("@sim", request.SimulationId);
                update.Parameters.AddWithValue("@parent", request.ParentArticleId);
                if (await update.ExecuteNonQueryAsync(cancellationToken) == 0)
                {
                    throw new InvalidOperationException("Ligne BOM introuvable ou non modifiable.");
                }
            }
            else
            {
                var existingLineId = await FindBomLineIdAsync(
                    connection,
                    request.SimulationId,
                    request.ParentArticleId,
                    request.ComponentArticleId,
                    nomenclatureType,
                    cancellationToken,
                    transaction);

                if (existingLineId is int mergeId)
                {
                    bomLineId = mergeId;
                    request = request with { BomLineId = mergeId };
                    await using var merge = connection.CreateCommand();
                    merge.Transaction = transaction;
                    merge.CommandText = """
                        UPDATE sim_bom_lines
                        SET quantity_per = @qty,
                            scrap_rate = @scrap,
                            offset_days = @offset,
                            apply_size_coefficient = @size,
                            apply_color_substitution = @color,
                            alternative = @alt
                        WHERE id = @id AND simulation_id = @sim AND parent_article_id = @parent AND is_generated = 0
                        """;
                    merge.Parameters.AddWithValue("@qty", request.QuantityPer);
                    merge.Parameters.AddWithValue("@scrap", request.ScrapRate);
                    merge.Parameters.AddWithValue("@offset", request.OffsetDays);
                    merge.Parameters.AddWithValue("@size", request.ApplySizeCoefficient);
                    merge.Parameters.AddWithValue("@color", request.ApplyColorSubstitution);
                    merge.Parameters.AddWithValue("@alt", alternative);
                    merge.Parameters.AddWithValue("@id", bomLineId);
                    merge.Parameters.AddWithValue("@sim", request.SimulationId);
                    merge.Parameters.AddWithValue("@parent", request.ParentArticleId);
                    await merge.ExecuteNonQueryAsync(cancellationToken);
                }
                else
                {
                    await using var insert = connection.CreateCommand();
                    insert.Transaction = transaction;
                    insert.CommandText = """
                        INSERT INTO sim_bom_lines (simulation_id, parent_article_id, component_article_id, quantity_per, scrap_rate, offset_days, apply_size_coefficient, apply_color_substitution, is_generated, is_simulated, nomenclature_type, alternative)
                        OUTPUT INSERTED.id
                        VALUES (@sim, @parent, @comp, @qty, @scrap, @offset, @size, @color, 0, 1, @nomType, @alt)
                        """;
                    insert.Parameters.AddWithValue("@sim", request.SimulationId);
                    insert.Parameters.AddWithValue("@parent", request.ParentArticleId);
                    insert.Parameters.AddWithValue("@comp", request.ComponentArticleId);
                    insert.Parameters.AddWithValue("@qty", request.QuantityPer);
                    insert.Parameters.AddWithValue("@scrap", request.ScrapRate);
                    insert.Parameters.AddWithValue("@offset", request.OffsetDays);
                    insert.Parameters.AddWithValue("@size", request.ApplySizeCoefficient);
                    insert.Parameters.AddWithValue("@color", request.ApplyColorSubstitution);
                    insert.Parameters.AddWithValue("@nomType", nomenclatureType);
                    insert.Parameters.AddWithValue("@alt", alternative);
                    bomLineId = (int)(await insert.ExecuteScalarAsync(cancellationToken) ?? throw new InvalidOperationException());
                }
            }

            await UpdateComponentUnitAsync(connection, request.SimulationId, request.ComponentArticleId, unit, cancellationToken, transaction);
            await transaction.CommitAsync(cancellationToken);

            return (await GetBomLinesAsync(request.SimulationId, cancellationToken))
                .First(l => l.Id == bomLineId);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task DeleteSimBomLineAsync(DeleteSimBomLineRequest request, CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);
        await using var connection = OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            DELETE bl
            FROM sim_bom_lines bl
            JOIN sim_articles p ON p.id = bl.parent_article_id
            WHERE bl.id = @id
              AND bl.simulation_id = @sim
              AND bl.is_generated = 0
              AND p.is_template = 1
            """;
        command.Parameters.AddWithValue("@id", request.BomLineId);
        command.Parameters.AddWithValue("@sim", request.SimulationId);
        if (await command.ExecuteNonQueryAsync(cancellationToken) == 0)
        {
            throw new InvalidOperationException("Ligne BOM introuvable ou non supprimable.");
        }
    }

    public async Task EnsureAlternativeNomenclaturesAsync(
        int simulationId,
        int parentArticleId,
        CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);
        await using var connection = OpenConnection();
        await EnsureEditableTemplateParentAsync(connection, simulationId, parentArticleId, cancellationToken);

        var allLines = (await GetBomLinesAsync(simulationId, cancellationToken))
            .Where(l => l.ParentArticleId == parentArticleId)
            .ToList();

        var baseLines = allLines
            .Where(l => string.Equals(l.NomenclatureType, SimulationNomenclatureTypes.Base, StringComparison.Ordinal))
            .ToList();

        if (baseLines.Count == 0)
        {
            return;
        }

        async Task EnsureTypeAsync(string nomenclatureType, int defaultAlternative)
        {
            var existing = allLines
                .Where(l => string.Equals(l.NomenclatureType, nomenclatureType, StringComparison.Ordinal))
                .ToDictionary(l => l.ComponentArticleId);

            foreach (var baseLine in baseLines)
            {
                if (existing.ContainsKey(baseLine.ComponentArticleId))
                {
                    continue;
                }

                await using var insert = connection.CreateCommand();
                insert.CommandText = """
                    INSERT INTO sim_bom_lines (simulation_id, parent_article_id, component_article_id, quantity_per, scrap_rate, offset_days, apply_size_coefficient, apply_color_substitution, is_generated, is_simulated, nomenclature_type, alternative)
                    VALUES (@sim, @parent, @comp, @qty, @scrap, @offset, @size, @color, 0, 1, @nomType, @alt)
                    """;
                insert.Parameters.AddWithValue("@sim", simulationId);
                insert.Parameters.AddWithValue("@parent", parentArticleId);
                insert.Parameters.AddWithValue("@comp", baseLine.ComponentArticleId);
                insert.Parameters.AddWithValue("@qty", baseLine.QuantityPer);
                insert.Parameters.AddWithValue("@scrap", baseLine.ScrapRate);
                insert.Parameters.AddWithValue("@offset", baseLine.OffsetDays);
                insert.Parameters.AddWithValue("@size", baseLine.ApplySizeCoefficient);
                insert.Parameters.AddWithValue("@color", baseLine.ApplyColorSubstitution);
                insert.Parameters.AddWithValue("@nomType", nomenclatureType);
                insert.Parameters.AddWithValue("@alt", defaultAlternative);
                await insert.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        await EnsureTypeAsync(SimulationNomenclatureTypes.Achat, SimulationNomenclatureTypes.DefaultAlternativeAchat);
        await EnsureTypeAsync(SimulationNomenclatureTypes.Production, SimulationNomenclatureTypes.DefaultAlternativeProduction);
    }

    public async Task<IReadOnlyList<SimulationArticleDto>> GetArticlesAsync(int simulationId, CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);
        await using var connection = OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, simulation_id, base_article_id, code, designation, article_type, size, color,
                   is_template, is_generated, procurement_type, lead_time_days, unit, llc
            FROM sim_articles WHERE simulation_id = @id
            ORDER BY is_template DESC, is_generated, code
            """;
        command.Parameters.AddWithValue("@id", simulationId);
        var list = new List<SimulationArticleDto>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(ReadArticle(reader));
        }

        return list;
    }

    public async Task<IReadOnlyList<SimulationBomLineDto>> GetBomLinesAsync(int simulationId, CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);
        await using var connection = OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT bl.id, bl.parent_article_id, p.code, bl.component_article_id, c.code,
                   bl.quantity_per, bl.scrap_rate, bl.offset_days, bl.apply_size_coefficient, bl.apply_color_substitution,
                   c.unit, bl.nomenclature_type, bl.alternative
            FROM sim_bom_lines bl
            JOIN sim_articles p ON p.id = bl.parent_article_id
            JOIN sim_articles c ON c.id = bl.component_article_id
            WHERE bl.simulation_id = @id
            ORDER BY p.code, c.code
            """;
        command.Parameters.AddWithValue("@id", simulationId);
        var list = new List<SimulationBomLineDto>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new SimulationBomLineDto(
                reader.GetInt32(0),
                reader.GetInt32(1),
                reader.GetString(2),
                reader.GetInt32(3),
                reader.GetString(4),
                reader.GetDouble(5),
                reader.GetDouble(6),
                reader.GetInt32(7),
                reader.GetBoolean(8),
                reader.GetBoolean(9),
                SimulationBomUnits.Normalize(reader.GetString(10)),
                SimulationNomenclatureTypes.Normalize(reader.GetString(11)),
                reader.GetInt32(12)));
        }

        return list;
    }

    public async Task<IReadOnlyList<SimulationRoutingOpDto>> GetRoutingOpsAsync(int simulationId, int? articleId = null, CancellationToken cancellationToken = default)
    {
        await using var connection = OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT r.id, r.article_id, a.code, r.operation_number, r.operation_name, r.work_center,
                   r.setup_time_minutes, r.run_time_minutes, r.apply_size_coefficient, r.apply_color_coefficient
            FROM sim_routing_operations r
            JOIN sim_articles a ON a.id = r.article_id
            WHERE r.simulation_id = @sim
              AND (@articleId IS NULL OR r.article_id = @articleId)
            ORDER BY a.code, r.operation_number
            """;
        command.Parameters.AddWithValue("@sim", simulationId);
        command.Parameters.AddWithValue("@articleId", (object?)articleId ?? DBNull.Value);
        var list = new List<SimulationRoutingOpDto>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new SimulationRoutingOpDto(
                reader.GetInt32(0),
                reader.GetInt32(1),
                reader.GetString(2),
                reader.GetInt32(3),
                reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.GetDouble(6),
                reader.GetDouble(7),
                reader.GetBoolean(8),
                reader.GetBoolean(9)));
        }

        return list;
    }

    public async Task<IReadOnlyList<SimulationCbnParameterDto>> GetCbnParametersAsync(int simulationId, CancellationToken cancellationToken = default)
    {
        await using var connection = OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT p.id, p.article_id, a.code, p.on_hand_stock, p.safety_stock, p.reserved_quantity,
                   p.scheduled_receipt_production, p.scheduled_receipt_purchase, p.lead_time_days,
                   p.lot_rule, p.min_lot, p.multiple_lot
            FROM sim_cbn_parameters p
            JOIN sim_articles a ON a.id = p.article_id
            WHERE p.simulation_id = @id
            ORDER BY a.code
            """;
        command.Parameters.AddWithValue("@id", simulationId);
        var list = new List<SimulationCbnParameterDto>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new SimulationCbnParameterDto(
                reader.GetInt32(0),
                reader.GetInt32(1),
                reader.GetString(2),
                reader.GetDouble(3),
                reader.GetDouble(4),
                reader.GetDouble(5),
                reader.GetDouble(6),
                reader.GetDouble(7),
                reader.GetInt32(8),
                reader.GetString(9),
                reader.GetDouble(10),
                reader.GetDouble(11)));
        }

        return list;
    }

    public async Task UpdateCbnParameterAsync(UpdateSimCbnParameterRequest request, CancellationToken cancellationToken = default)
    {
        await using var connection = OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE sim_cbn_parameters
            SET on_hand_stock = @stock,
                safety_stock = @safety,
                reserved_quantity = @reserved,
                scheduled_receipt_production = @mfg,
                scheduled_receipt_purchase = @purch,
                lead_time_days = @lead,
                lot_rule = @lot,
                min_lot = @minLot,
                multiple_lot = @multiple
            WHERE simulation_id = @sim AND article_id = @art
            """;
        command.Parameters.AddWithValue("@sim", request.SimulationId);
        command.Parameters.AddWithValue("@art", request.ArticleId);
        command.Parameters.AddWithValue("@stock", request.OnHandStock);
        command.Parameters.AddWithValue("@safety", request.SafetyStock);
        command.Parameters.AddWithValue("@reserved", request.ReservedQuantity);
        command.Parameters.AddWithValue("@mfg", request.ScheduledReceiptProduction);
        command.Parameters.AddWithValue("@purch", request.ScheduledReceiptPurchase);
        command.Parameters.AddWithValue("@lead", request.LeadTimeDays);
        command.Parameters.AddWithValue("@lot", request.LotRule);
        command.Parameters.AddWithValue("@minLot", request.MinLot);
        command.Parameters.AddWithValue("@multiple", request.MultipleLot);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SimulationSalesOrderDto>> GetSalesOrdersAsync(int simulationId, CancellationToken cancellationToken = default)
    {
        await using var connection = OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT so.id, so.simulation_id, so.customer_name, so.customer_order_number, so.article_id, a.code,
                   so.quantity, so.requested_date, so.priority
            FROM sim_sales_orders so
            JOIN sim_articles a ON a.id = so.article_id
            WHERE so.simulation_id = @id
            ORDER BY so.created_at DESC
            """;
        command.Parameters.AddWithValue("@id", simulationId);
        var list = new List<SimulationSalesOrderDto>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new SimulationSalesOrderDto(
                reader.GetInt32(0),
                reader.GetInt32(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetInt32(4),
                reader.GetString(5),
                reader.GetDouble(6),
                DateOnly.FromDateTime(reader.GetDateTime(7)),
                reader.GetInt32(8)));
        }

        return list;
    }

    public async Task<SimulationSalesOrderDto> UpsertSalesOrderAsync(UpsertSalesOrderRequest request, CancellationToken cancellationToken = default)
    {
        await using var connection = OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO sim_sales_orders (simulation_id, customer_name, customer_order_number, article_id, quantity, requested_date, priority)
            OUTPUT INSERTED.id
            VALUES (@sim, @customer, @number, @art, @qty, @date, @prio)
            """;
        command.Parameters.AddWithValue("@sim", request.SimulationId);
        command.Parameters.AddWithValue("@customer", request.CustomerName);
        command.Parameters.AddWithValue("@number", request.CustomerOrderNumber);
        command.Parameters.AddWithValue("@art", request.ArticleId);
        command.Parameters.AddWithValue("@qty", request.Quantity);
        command.Parameters.AddWithValue("@date", request.RequestedDate.ToDateTime(TimeOnly.MinValue));
        command.Parameters.AddWithValue("@prio", request.Priority);
        var id = (int)(await command.ExecuteScalarAsync(cancellationToken) ?? throw new InvalidOperationException());
        var articles = await GetArticlesAsync(request.SimulationId, cancellationToken);
        var article = articles.First(a => a.Id == request.ArticleId);
        return new SimulationSalesOrderDto(
            id,
            request.SimulationId,
            request.CustomerName,
            request.CustomerOrderNumber,
            request.ArticleId,
            article.Code,
            request.Quantity,
            request.RequestedDate,
            request.Priority);
    }

    public async Task<GenerateVariantsResult> GenerateVariantsAsync(GenerateVariantsRequest request, CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);
        await using var connection = OpenConnection();

        var template = (await GetArticlesAsync(request.SimulationId, cancellationToken))
            .FirstOrDefault(a => a.Id == request.TemplateArticleId)
            ?? throw new InvalidOperationException("Article template introuvable.");

        var bom = await LoadTemplateBomAsync(connection, request.SimulationId, request.TemplateArticleId, cancellationToken);
        var routing = await LoadTemplateRoutingAsync(connection, request.SimulationId, request.TemplateArticleId, cancellationToken);
        var substitutions = await LoadSubstitutionsAsync(connection, request.SimulationId, cancellationToken);
        var allArticles = await GetArticlesAsync(request.SimulationId, cancellationToken);
        var codesById = allArticles.ToDictionary(a => a.Id, a => a.Code);

        var generated = new List<SimulationArticleDto>();
        foreach (var size in request.Sizes)
        {
            foreach (var color in request.Colors)
            {
                var artifact = SimulationDuplicationEngine.GenerateVariant(
                    template.Code,
                    template.Designation,
                    size.Size,
                    color.Color,
                    size.Coefficient,
                    color.Coefficient,
                    bom,
                    routing,
                    substitutions,
                    codesById);

                // skip if already exists
                var existing = allArticles.FirstOrDefault(a => a.Code == artifact.Code);
                int variantId;
                if (existing is not null)
                {
                    variantId = existing.Id;
                    await DeleteGeneratedBomAndRoutingAsync(connection, request.SimulationId, variantId, cancellationToken);
                    await InsertGeneratedBomAsync(connection, request.SimulationId, variantId, artifact, codesById, cancellationToken);
                    await InsertGeneratedRoutingAsync(connection, request.SimulationId, variantId, artifact, cancellationToken);
                }
                else
                {
                    variantId = await InsertGeneratedArticleAsync(connection, request.SimulationId, template.Id, artifact, cancellationToken);
                    await InsertGeneratedBomAsync(connection, request.SimulationId, variantId, artifact, codesById, cancellationToken);
                    await InsertGeneratedRoutingAsync(connection, request.SimulationId, variantId, artifact, cancellationToken);
                    await EnsureGeneratedCbnParamsAsync(connection, request.SimulationId, variantId, template.LeadTimeDays, cancellationToken);
                    await InsertVariantDefinitionAsync(connection, request.SimulationId, template.Id, artifact, cancellationToken);
                }

                generated.Add(new SimulationArticleDto(
                    variantId,
                    request.SimulationId,
                    template.Id,
                    artifact.Code,
                    artifact.Designation,
                    SimulationArticleTypes.FinishedGood,
                    artifact.Size,
                    artifact.Color,
                    false,
                    true,
                    SimulationProcurementTypes.Manufactured,
                    template.LeadTimeDays,
                    "PCS",
                    null));
            }
        }

        return new GenerateVariantsResult(request.SimulationId, generated);
    }

    public async Task<CbnRunResultDto> RunCbnAsync(CbnRunRequestDto request, CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);
        await using var connection = OpenConnection();

        var articles = await GetArticlesAsync(request.SimulationId, cancellationToken);
        var bomDtos = await GetBomLinesAsync(request.SimulationId, cancellationToken);
        var paramDtos = await GetCbnParametersAsync(request.SimulationId, cancellationToken);

        var articleInputs = articles.Select(a => new SimArticleInput(
            a.Id, a.Code, a.Designation, a.ArticleType, a.ProcurementType, a.LeadTimeDays, a.Size, a.Color)).ToList();
        var bomInputs = bomDtos.Select(b => new SimBomLineInput(
            b.Id, b.ParentArticleId, b.ComponentArticleId, b.QuantityPer, b.ScrapRate, b.OffsetDays)).ToList();
        var parameters = paramDtos.ToDictionary(
            p => p.ArticleId,
            p => new SimCbnParameterInput(
                p.ArticleId, p.OnHandStock, p.SafetyStock, p.ReservedQuantity,
                p.ScheduledReceiptProduction, p.ScheduledReceiptPurchase,
                p.LeadTimeDays, p.LotRule, p.MinLot, p.MultipleLot));

        var salesOrder = new SimSalesOrderInput(
            request.SalesOrderId,
            "CMD",
            request.ArticleId,
            request.Quantity,
            request.RequestedDate);

        // Enrich order number from DB if present
        var customerName = "Client";
        await using (var soCmd = connection.CreateCommand())
        {
            soCmd.CommandText = "SELECT customer_order_number, customer_name FROM sim_sales_orders WHERE id = @id";
            soCmd.Parameters.AddWithValue("@id", request.SalesOrderId);
            await using var reader = await soCmd.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                var number = reader.IsDBNull(0) ? null : reader.GetString(0);
                customerName = reader.IsDBNull(1) ? customerName : reader.GetString(1);
                if (!string.IsNullOrWhiteSpace(number))
                {
                    salesOrder = salesOrder with { CustomerOrderNumber = number };
                }
            }
        }

        var today = DateOnly.FromDateTime(DateTime.Today);
        var result = SimulationCbnEngine.Run(salesOrder, articleInputs, bomInputs, parameters, today);

        // persist LLC
        foreach (var (articleId, code) in result.LowLevelCodes)
        {
            await using var llcCmd = connection.CreateCommand();
            llcCmd.CommandText = "UPDATE sim_articles SET llc = @llc WHERE id = @id AND simulation_id = @sim";
            llcCmd.Parameters.AddWithValue("@llc", code);
            llcCmd.Parameters.AddWithValue("@id", articleId);
            llcCmd.Parameters.AddWithValue("@sim", request.SimulationId);
            await llcCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        var runId = await InsertCbnRunAsync(
            connection,
            request,
            salesOrder.CustomerOrderNumber,
            customerName,
            result,
            cancellationToken);

        await PersistResultsAsync(connection, request, runId, result, cancellationToken);

        var codeById = articles.ToDictionary(a => a.Id, a => a.Code);
        return MapResult(runId, result, codeById);
    }

    private static async Task<int> InsertCbnRunAsync(
        SqlConnection connection,
        CbnRunRequestDto request,
        string orderNumber,
        string customerName,
        SimulationCbnRunResult result,
        CancellationToken cancellationToken)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO sim_cbn_runs (simulation_id, sales_order_id, article_id, quantity, requested_date, customer_name, customer_order_number, planned_order_count, alert_count, net_line_count)
            OUTPUT INSERTED.id
            VALUES (@sim, @so, @art, @qty, @date, @cust, @ord, @planned, @alerts, @nets)
            """;
        cmd.Parameters.AddWithValue("@sim", request.SimulationId);
        cmd.Parameters.AddWithValue("@so", request.SalesOrderId);
        cmd.Parameters.AddWithValue("@art", request.ArticleId);
        cmd.Parameters.AddWithValue("@qty", request.Quantity);
        cmd.Parameters.AddWithValue("@date", request.RequestedDate.ToDateTime(TimeOnly.MinValue));
        cmd.Parameters.AddWithValue("@cust", customerName);
        cmd.Parameters.AddWithValue("@ord", orderNumber);
        cmd.Parameters.AddWithValue("@planned", result.PlannedOrders.Count);
        cmd.Parameters.AddWithValue("@alerts", result.Alerts.Count);
        cmd.Parameters.AddWithValue("@nets", result.NetRequirements.Count);
        return (int)(await cmd.ExecuteScalarAsync(cancellationToken) ?? 0);
    }

    public async Task<IReadOnlyList<SimCbnRunHistoryDto>> ListCbnRunsAsync(int simulationId, CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);
        await using var connection = OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT r.id, r.simulation_id, r.run_at, a.code, r.customer_name, r.customer_order_number,
                   r.quantity, r.requested_date, r.planned_order_count, r.alert_count, r.net_line_count
            FROM sim_cbn_runs r
            INNER JOIN sim_articles a ON a.id = r.article_id
            WHERE r.simulation_id = @sim
            ORDER BY r.run_at DESC
            """;
        command.Parameters.AddWithValue("@sim", simulationId);
        var list = new List<SimCbnRunHistoryDto>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new SimCbnRunHistoryDto(
                reader.GetInt32(0),
                reader.GetInt32(1),
                reader.GetDateTime(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetDouble(6),
                DateOnly.FromDateTime(reader.GetDateTime(7)),
                reader.GetInt32(8),
                reader.GetInt32(9),
                reader.GetInt32(10)));
        }

        return list;
    }

    public async Task<CbnRunResultDto?> GetCbnRunResultAsync(int runId, CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);
        await using var connection = OpenConnection();

        await using (var check = connection.CreateCommand())
        {
            check.CommandText = "SELECT simulation_id FROM sim_cbn_runs WHERE id = @id";
            check.Parameters.AddWithValue("@id", runId);
            if (await check.ExecuteScalarAsync(cancellationToken) is not int simulationId)
            {
                return null;
            }

            var articles = await GetArticlesAsync(simulationId, cancellationToken);
            var codeById = articles.ToDictionary(a => a.Id, a => a.Code);

            var flatBom = await LoadFlatBomLinesAsync(connection, runId, cancellationToken);
            var gross = await LoadGrossRequirementsAsync(connection, runId, codeById, cancellationToken);
            var net = await LoadNetRequirementsAsync(connection, runId, codeById, cancellationToken);
            var planned = await LoadPlannedOrdersAsync(connection, runId, codeById, cancellationToken);
            var workOrders = await LoadWorkOrdersAsync(connection, runId, codeById, cancellationToken);
            var pegging = await LoadPeggingTreeAsync(connection, runId, codeById, cancellationToken);
            var alerts = await LoadAlertsAsync(connection, runId, codeById, cancellationToken);
            var traces = BuildTraceSteps(net, gross, planned);

            return new CbnRunResultDto(runId, flatBom, gross, net, planned, workOrders, pegging, alerts, traces);
        }
    }

    private static List<SimCbnTraceStepDto> BuildTraceSteps(
        IReadOnlyList<NetRequirementDto> netRequirements,
        IReadOnlyList<GrossRequirementDto> grossRequirements,
        IReadOnlyList<PlannedOrderDto> plannedOrders)
    {
        var levelByArticle = grossRequirements
            .GroupBy(g => g.ArticleCode)
            .ToDictionary(g => g.Key, g => g.Min(x => x.Level));
        var pathByArticle = grossRequirements
            .Where(g => !string.IsNullOrWhiteSpace(g.Path))
            .GroupBy(g => g.ArticleCode)
            .ToDictionary(g => g.Key, g => g.First().Path);
        var plannedByArticle = plannedOrders
            .GroupBy(p => p.ArticleCode)
            .ToDictionary(g => g.Key, g => g.First());

        return netRequirements
            .OrderBy(n => levelByArticle.GetValueOrDefault(n.ArticleCode, 0))
            .ThenBy(n => n.ArticleCode)
            .Select(n =>
            {
                plannedByArticle.TryGetValue(n.ArticleCode, out var planned);
                return new SimCbnTraceStepDto(
                    n.ArticleCode,
                    levelByArticle.GetValueOrDefault(n.ArticleCode, 0),
                    n.GrossRequirement,
                    n.OnHandStock,
                    n.SafetyStock,
                    n.ReservedQuantity,
                    n.ScheduledReceiptProduction,
                    n.ScheduledReceiptPurchase,
                    n.NetRequirement,
                    n.NeedDate,
                    planned?.OrderType,
                    planned?.Quantity,
                    planned?.ReleaseDate,
                    planned?.ReceiptDate,
                    planned?.Justification,
                    pathByArticle.GetValueOrDefault(n.ArticleCode));
            })
            .ToList();
    }

    private async Task PersistResultsAsync(
        SqlConnection connection,
        CbnRunRequestDto request,
        int runId,
        SimulationCbnRunResult result,
        CancellationToken cancellationToken)
    {
        foreach (var f in result.FlatBomLines)
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                INSERT INTO sim_flat_bom_lines (cbn_run_id, simulation_id, level, parent_article_code, component_article_code, quantity_per, cumulative_quantity, need_date, path)
                VALUES (@run, @sim, @level, @parent, @comp, @qty, @cumul, @date, @path)
                """;
            cmd.Parameters.AddWithValue("@run", runId);
            cmd.Parameters.AddWithValue("@sim", request.SimulationId);
            cmd.Parameters.AddWithValue("@level", f.Level);
            cmd.Parameters.AddWithValue("@parent", (object?)f.ParentArticleCode ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@comp", f.ComponentArticleCode);
            cmd.Parameters.AddWithValue("@qty", f.QuantityPer);
            cmd.Parameters.AddWithValue("@cumul", f.CumulativeQuantity);
            cmd.Parameters.AddWithValue("@date", f.NeedDate.ToDateTime(TimeOnly.MinValue));
            cmd.Parameters.AddWithValue("@path", f.Path);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var g in result.GrossRequirements)
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                INSERT INTO sim_gross_requirements (simulation_id, sales_order_id, article_id, parent_article_id, level, gross_quantity, need_date, source_type, path, cbn_run_id)
                VALUES (@sim, @so, @art, @parent, @level, @qty, @date, @source, @path, @run)
                """;
            cmd.Parameters.AddWithValue("@sim", request.SimulationId);
            cmd.Parameters.AddWithValue("@so", request.SalesOrderId);
            cmd.Parameters.AddWithValue("@art", g.ArticleId);
            cmd.Parameters.AddWithValue("@parent", (object?)g.ParentArticleId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@level", g.Level);
            cmd.Parameters.AddWithValue("@qty", g.GrossQuantity);
            cmd.Parameters.AddWithValue("@date", g.NeedDate.ToDateTime(TimeOnly.MinValue));
            cmd.Parameters.AddWithValue("@source", g.SourceType);
            cmd.Parameters.AddWithValue("@path", (object?)g.Path ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@run", runId);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var n in result.NetRequirements)
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                INSERT INTO sim_net_requirements (simulation_id, sales_order_id, article_id, gross_requirement, on_hand_stock, safety_stock, reserved_quantity, scheduled_receipt_production, scheduled_receipt_purchase, net_requirement, need_date, cbn_run_id)
                VALUES (@sim, @so, @art, @gross, @stock, @safety, @reserved, @mfg, @purch, @net, @date, @run)
                """;
            cmd.Parameters.AddWithValue("@sim", request.SimulationId);
            cmd.Parameters.AddWithValue("@so", request.SalesOrderId);
            cmd.Parameters.AddWithValue("@art", n.ArticleId);
            cmd.Parameters.AddWithValue("@gross", n.GrossRequirement);
            cmd.Parameters.AddWithValue("@stock", n.OnHandStock);
            cmd.Parameters.AddWithValue("@safety", n.SafetyStock);
            cmd.Parameters.AddWithValue("@reserved", n.ReservedQuantity);
            cmd.Parameters.AddWithValue("@mfg", n.ScheduledReceiptProduction);
            cmd.Parameters.AddWithValue("@purch", n.ScheduledReceiptPurchase);
            cmd.Parameters.AddWithValue("@net", n.NetRequirement);
            cmd.Parameters.AddWithValue("@date", n.NeedDate.ToDateTime(TimeOnly.MinValue));
            cmd.Parameters.AddWithValue("@run", runId);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        var plannedIds = new Dictionary<(int ArticleId, string OrderType), int>();
        foreach (var p in result.PlannedOrders)
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                INSERT INTO sim_planned_orders (simulation_id, sales_order_id, article_id, order_type, quantity, need_date, release_date, receipt_date, status, source_type, cbn_run_id, justification)
                OUTPUT INSERTED.id
                VALUES (@sim, @so, @art, @type, @qty, @need, @release, @receipt, 'PROPOSED', @source, @run, @just)
                """;
            cmd.Parameters.AddWithValue("@sim", request.SimulationId);
            cmd.Parameters.AddWithValue("@so", request.SalesOrderId);
            cmd.Parameters.AddWithValue("@art", p.ArticleId);
            cmd.Parameters.AddWithValue("@type", p.OrderType);
            cmd.Parameters.AddWithValue("@qty", p.Quantity);
            cmd.Parameters.AddWithValue("@need", p.NeedDate.ToDateTime(TimeOnly.MinValue));
            cmd.Parameters.AddWithValue("@release", p.ReleaseDate.ToDateTime(TimeOnly.MinValue));
            cmd.Parameters.AddWithValue("@receipt", p.ReceiptDate.ToDateTime(TimeOnly.MinValue));
            cmd.Parameters.AddWithValue("@source", p.SourceType);
            cmd.Parameters.AddWithValue("@run", runId);
            cmd.Parameters.AddWithValue("@just", p.Justification);
            var plannedId = (int)(await cmd.ExecuteScalarAsync(cancellationToken) ?? 0);
            plannedIds[(p.ArticleId, p.OrderType)] = plannedId;

            if (p.OrderType == SimulationOrderTypes.Of)
            {
                await using var wo = connection.CreateCommand();
                wo.CommandText = """
                    INSERT INTO sim_work_orders (simulation_id, planned_order_id, sales_order_id, article_id, quantity, start_date, end_date, status, priority, cbn_run_id)
                    VALUES (@sim, @planned, @so, @art, @qty, @start, @end, 'PLANNED', 50, @run)
                    """;
                wo.Parameters.AddWithValue("@sim", request.SimulationId);
                wo.Parameters.AddWithValue("@planned", plannedId);
                wo.Parameters.AddWithValue("@so", request.SalesOrderId);
                wo.Parameters.AddWithValue("@art", p.ArticleId);
                wo.Parameters.AddWithValue("@qty", p.Quantity);
                wo.Parameters.AddWithValue("@start", p.ReleaseDate.ToDateTime(TimeOnly.MinValue));
                wo.Parameters.AddWithValue("@end", p.ReceiptDate.ToDateTime(TimeOnly.MinValue));
                wo.Parameters.AddWithValue("@run", runId);
                await wo.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        void PersistPegging(IEnumerable<PeggingNodeResult> nodes)
        {
            foreach (var node in nodes)
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = """
                    INSERT INTO sim_pegging (simulation_id, sales_order_id, planned_order_id, parent_article_id, component_article_id, level, quantity, need_date, source_type, path, cbn_run_id)
                    VALUES (@sim, @so, @planned, @parent, @comp, @level, @qty, @date, @source, @path, @run)
                    """;
                cmd.Parameters.AddWithValue("@sim", request.SimulationId);
                cmd.Parameters.AddWithValue("@so", request.SalesOrderId);
                var plannedId = plannedIds.TryGetValue((node.ComponentArticleId, SimulationOrderTypes.Of), out var ofId)
                    ? ofId
                    : plannedIds.TryGetValue((node.ComponentArticleId, SimulationOrderTypes.Oa), out var oaId) ? oaId : (int?)null;
                cmd.Parameters.AddWithValue("@planned", (object?)plannedId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@parent", (object?)node.ParentArticleId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@comp", node.ComponentArticleId);
                cmd.Parameters.AddWithValue("@level", node.Level);
                cmd.Parameters.AddWithValue("@qty", node.Quantity);
                cmd.Parameters.AddWithValue("@date", node.NeedDate.ToDateTime(TimeOnly.MinValue));
                cmd.Parameters.AddWithValue("@source", node.SourceType);
                cmd.Parameters.AddWithValue("@path", node.Path);
                cmd.Parameters.AddWithValue("@run", runId);
                cmd.ExecuteNonQuery();
                PersistPegging(node.Children);
            }
        }

        PersistPegging(result.PeggingTree);

        foreach (var alert in result.Alerts)
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                INSERT INTO sim_cbn_alerts (simulation_id, article_id, alert_type, message, severity, cbn_run_id)
                VALUES (@sim, @art, @type, @msg, @sev, @run)
                """;
            cmd.Parameters.AddWithValue("@sim", request.SimulationId);
            cmd.Parameters.AddWithValue("@art", (object?)alert.ArticleId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@type", alert.AlertType);
            cmd.Parameters.AddWithValue("@msg", alert.Message);
            cmd.Parameters.AddWithValue("@sev", alert.Severity);
            cmd.Parameters.AddWithValue("@run", runId);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static CbnRunResultDto MapResult(int runId, SimulationCbnRunResult result, IReadOnlyDictionary<int, string> codeById)
    {
        PeggingNodeDto MapPeg(PeggingNodeResult node) => new(
            node.ArticleCode,
            node.Quantity,
            node.Level,
            node.NeedDate,
            node.SourceType,
            node.Path,
            node.Children.Select(MapPeg).ToList());

        var gross = result.GrossRequirements.Select(g => new GrossRequirementDto(
            g.ArticleCode, g.Level, g.GrossQuantity, g.NeedDate, g.SourceType, g.Path)).ToList();
        var net = result.NetRequirements.Select(n => new NetRequirementDto(
            n.ArticleCode, n.GrossRequirement, n.OnHandStock, n.SafetyStock, n.ReservedQuantity,
            n.ScheduledReceiptProduction, n.ScheduledReceiptPurchase, n.NetRequirement, n.NeedDate)).ToList();
        var planned = result.PlannedOrders.Select(p => new PlannedOrderDto(
            p.OrderType, p.ArticleCode, p.Quantity, p.ReleaseDate, p.ReceiptDate, p.SourceType, p.Justification)).ToList();

        return new CbnRunResultDto(
            runId,
            result.FlatBomLines.Select(f => new FlatBomLineDto(
                f.Level, f.ParentArticleCode, f.ComponentArticleCode, f.QuantityPer, f.CumulativeQuantity, f.NeedDate, f.Path)).ToList(),
            gross,
            net,
            planned,
            result.WorkOrders.Select(w => new WorkOrderDto(
                w.ArticleCode, w.Quantity, w.StartDate, w.EndDate, w.Status)).ToList(),
            result.PeggingTree.Select(MapPeg).ToList(),
            result.Alerts.Select(a => new CbnAlertDto(
                a.ArticleId is int id && codeById.TryGetValue(id, out var code) ? code : null,
                a.AlertType, a.Message, a.Severity)).ToList(),
            BuildTraceSteps(net, gross, planned));
    }

    private static async Task<IReadOnlyList<FlatBomLineDto>> LoadFlatBomLinesAsync(SqlConnection connection, int runId, CancellationToken cancellationToken)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT level, parent_article_code, component_article_code, quantity_per, cumulative_quantity, need_date, path
            FROM sim_flat_bom_lines
            WHERE cbn_run_id = @run
            ORDER BY level, path
            """;
        cmd.Parameters.AddWithValue("@run", runId);
        var list = new List<FlatBomLineDto>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new FlatBomLineDto(
                reader.GetInt32(0),
                reader.IsDBNull(1) ? null : reader.GetString(1),
                reader.GetString(2),
                reader.GetDouble(3),
                reader.GetDouble(4),
                DateOnly.FromDateTime(reader.GetDateTime(5)),
                reader.GetString(6)));
        }

        return list;
    }

    private static async Task<IReadOnlyList<GrossRequirementDto>> LoadGrossRequirementsAsync(
        SqlConnection connection,
        int runId,
        IReadOnlyDictionary<int, string> codeById,
        CancellationToken cancellationToken)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT g.level, g.gross_quantity, g.need_date, g.source_type, g.path, g.article_id
            FROM sim_gross_requirements g
            WHERE g.cbn_run_id = @run
            ORDER BY g.level, g.path
            """;
        cmd.Parameters.AddWithValue("@run", runId);
        var list = new List<GrossRequirementDto>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var articleId = reader.GetInt32(5);
            list.Add(new GrossRequirementDto(
                codeById.GetValueOrDefault(articleId, articleId.ToString()),
                reader.GetInt32(0),
                reader.GetDouble(1),
                DateOnly.FromDateTime(reader.GetDateTime(2)),
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4)));
        }

        return list;
    }

    private static async Task<IReadOnlyList<NetRequirementDto>> LoadNetRequirementsAsync(
        SqlConnection connection,
        int runId,
        IReadOnlyDictionary<int, string> codeById,
        CancellationToken cancellationToken)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT n.gross_requirement, n.on_hand_stock, n.safety_stock, n.reserved_quantity,
                   n.scheduled_receipt_production, n.scheduled_receipt_purchase, n.net_requirement, n.need_date, n.article_id
            FROM sim_net_requirements n
            WHERE n.cbn_run_id = @run
            ORDER BY n.need_date, n.article_id
            """;
        cmd.Parameters.AddWithValue("@run", runId);
        var list = new List<NetRequirementDto>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var articleId = reader.GetInt32(8);
            list.Add(new NetRequirementDto(
                codeById.GetValueOrDefault(articleId, articleId.ToString()),
                reader.GetDouble(0),
                reader.GetDouble(1),
                reader.GetDouble(2),
                reader.GetDouble(3),
                reader.GetDouble(4),
                reader.GetDouble(5),
                reader.GetDouble(6),
                DateOnly.FromDateTime(reader.GetDateTime(7))));
        }

        return list;
    }

    private static async Task<IReadOnlyList<PlannedOrderDto>> LoadPlannedOrdersAsync(
        SqlConnection connection,
        int runId,
        IReadOnlyDictionary<int, string> codeById,
        CancellationToken cancellationToken)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT p.order_type, p.quantity, p.release_date, p.receipt_date, p.source_type, p.justification, p.article_id
            FROM sim_planned_orders p
            WHERE p.cbn_run_id = @run
            ORDER BY p.release_date, p.article_id
            """;
        cmd.Parameters.AddWithValue("@run", runId);
        var list = new List<PlannedOrderDto>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var articleId = reader.GetInt32(6);
            list.Add(new PlannedOrderDto(
                reader.GetString(0),
                codeById.GetValueOrDefault(articleId, articleId.ToString()),
                reader.GetDouble(1),
                DateOnly.FromDateTime(reader.GetDateTime(2)),
                DateOnly.FromDateTime(reader.GetDateTime(3)),
                reader.GetString(4),
                reader.IsDBNull(5) ? string.Empty : reader.GetString(5)));
        }

        return list;
    }

    private static async Task<IReadOnlyList<WorkOrderDto>> LoadWorkOrdersAsync(
        SqlConnection connection,
        int runId,
        IReadOnlyDictionary<int, string> codeById,
        CancellationToken cancellationToken)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT w.quantity, w.start_date, w.end_date, w.status, w.article_id
            FROM sim_work_orders w
            WHERE w.cbn_run_id = @run
            ORDER BY w.start_date
            """;
        cmd.Parameters.AddWithValue("@run", runId);
        var list = new List<WorkOrderDto>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var articleId = reader.GetInt32(4);
            list.Add(new WorkOrderDto(
                codeById.GetValueOrDefault(articleId, articleId.ToString()),
                reader.GetDouble(0),
                DateOnly.FromDateTime(reader.GetDateTime(1)),
                DateOnly.FromDateTime(reader.GetDateTime(2)),
                reader.GetString(3)));
        }

        return list;
    }

    private static async Task<IReadOnlyList<PeggingNodeDto>> LoadPeggingTreeAsync(
        SqlConnection connection,
        int runId,
        IReadOnlyDictionary<int, string> codeById,
        CancellationToken cancellationToken)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT p.component_article_id, p.parent_article_id, p.level, p.quantity, p.need_date, p.source_type, p.path
            FROM sim_pegging p
            WHERE p.cbn_run_id = @run
            ORDER BY p.level, p.path
            """;
        cmd.Parameters.AddWithValue("@run", runId);
        var rows = new List<(int ComponentId, int? ParentId, int Level, double Qty, DateOnly NeedDate, string Source, string Path)>();
        await using (var reader = await cmd.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                rows.Add((
                    reader.GetInt32(0),
                    reader.IsDBNull(1) ? null : reader.GetInt32(1),
                    reader.GetInt32(2),
                    reader.GetDouble(3),
                    DateOnly.FromDateTime(reader.GetDateTime(4)),
                    reader.GetString(5),
                    reader.GetString(6)));
            }
        }

        if (rows.Count == 0)
        {
            return [];
        }

        var byParent = rows
            .Where(r => r.ParentId is not null)
            .GroupBy(r => r.ParentId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());
        var rootRows = rows.Where(r => r.Level == 0 || r.ParentId is null).ToList();
        if (rootRows.Count == 0)
        {
            rootRows = [rows[0]];
        }

        PeggingNodeDto Map(int componentId, int? parentId, int level, double qty, DateOnly needDate, string source, string path)
        {
            var children = byParent.GetValueOrDefault(componentId)?
                .Select(c => Map(c.ComponentId, componentId, c.Level, c.Qty, c.NeedDate, c.Source, c.Path))
                .ToList() ?? [];
            return new PeggingNodeDto(
                codeById.GetValueOrDefault(componentId, componentId.ToString()),
                qty,
                level,
                needDate,
                source,
                path,
                children);
        }

        return rootRows
            .Select(r => Map(r.ComponentId, r.ParentId, r.Level, r.Qty, r.NeedDate, r.Source, r.Path))
            .ToList();
    }

    private static async Task<IReadOnlyList<CbnAlertDto>> LoadAlertsAsync(
        SqlConnection connection,
        int runId,
        IReadOnlyDictionary<int, string> codeById,
        CancellationToken cancellationToken)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT a.alert_type, a.message, a.severity, a.article_id
            FROM sim_cbn_alerts a
            WHERE a.cbn_run_id = @run
            ORDER BY a.id
            """;
        cmd.Parameters.AddWithValue("@run", runId);
        var list = new List<CbnAlertDto>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            string? code = null;
            if (!reader.IsDBNull(3))
            {
                var articleId = reader.GetInt32(3);
                codeById.TryGetValue(articleId, out code);
            }

            list.Add(new CbnAlertDto(
                code,
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2)));
        }

        return list;
    }

    private static SimulationArticleDto ReadArticle(SqlDataReader reader)
        => new(
            reader.GetInt32(0),
            reader.GetInt32(1),
            reader.IsDBNull(2) ? null : reader.GetInt32(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.IsDBNull(6) ? null : reader.GetString(6),
            reader.IsDBNull(7) ? null : reader.GetString(7),
            reader.GetBoolean(8),
            reader.GetBoolean(9),
            reader.GetString(10),
            reader.GetInt32(11),
            reader.GetString(12),
            reader.IsDBNull(13) ? null : reader.GetInt32(13));

    private static string BuildTemplateDesignation(string articleName, string code)
    {
        var trimmed = articleName.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return code;
        }

        return trimmed.EndsWith("_BASE", StringComparison.OrdinalIgnoreCase)
            ? trimmed
            : $"{trimmed} (template)";
    }

    private static async Task<int?> FindArticleIdByCodeAsync(
        SqlConnection connection,
        int simulationId,
        string code,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT id FROM sim_articles WHERE simulation_id = @sim AND code = @code";
        command.Parameters.AddWithValue("@sim", simulationId);
        command.Parameters.AddWithValue("@code", code);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is null or DBNull ? null : Convert.ToInt32(result);
    }

    private static async Task<int?> FindBomLineIdAsync(
        SqlConnection connection,
        int simulationId,
        int parentArticleId,
        int componentArticleId,
        string nomenclatureType,
        CancellationToken cancellationToken,
        SqlTransaction? transaction = null)
    {
        await using var command = connection.CreateCommand();
        if (transaction is not null)
        {
            command.Transaction = transaction;
        }

        command.CommandText = """
            SELECT id FROM sim_bom_lines
            WHERE simulation_id = @sim AND parent_article_id = @parent AND component_article_id = @comp
              AND nomenclature_type = @nomType AND is_generated = 0
            """;
        command.Parameters.AddWithValue("@sim", simulationId);
        command.Parameters.AddWithValue("@parent", parentArticleId);
        command.Parameters.AddWithValue("@comp", componentArticleId);
        command.Parameters.AddWithValue("@nomType", SimulationNomenclatureTypes.Normalize(nomenclatureType));
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is null or DBNull ? null : Convert.ToInt32(result);
    }

    private static async Task EnsureEditableTemplateParentAsync(
        SqlConnection connection,
        int simulationId,
        int parentArticleId,
        CancellationToken cancellationToken,
        SqlTransaction? transaction = null)
    {
        await using var command = connection.CreateCommand();
        if (transaction is not null)
        {
            command.Transaction = transaction;
        }

        command.CommandText = """
            SELECT COUNT(*) FROM sim_articles
            WHERE id = @id AND simulation_id = @sim AND is_template = 1 AND is_generated = 0
            """;
        command.Parameters.AddWithValue("@id", parentArticleId);
        command.Parameters.AddWithValue("@sim", simulationId);
        if (Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) == 0)
        {
            throw new InvalidOperationException("Seule la nomenclature d'un article template peut etre modifiee.");
        }
    }

    private static async Task EnsureArticleBelongsToSimulationAsync(
        SqlConnection connection,
        int simulationId,
        int articleId,
        CancellationToken cancellationToken,
        SqlTransaction? transaction = null)
    {
        await using var command = connection.CreateCommand();
        if (transaction is not null)
        {
            command.Transaction = transaction;
        }

        command.CommandText = "SELECT COUNT(*) FROM sim_articles WHERE id = @id AND simulation_id = @sim";
        command.Parameters.AddWithValue("@id", articleId);
        command.Parameters.AddWithValue("@sim", simulationId);
        if (Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) == 0)
        {
            throw new InvalidOperationException("Composant introuvable dans cette simulation.");
        }
    }

    private static async Task<int> InsertSimArticleAsync(
        SqlConnection connection,
        int simulationId,
        string code,
        string designation,
        string articleType,
        string procurementType,
        int leadTimeDays,
        bool isTemplate,
        string unit,
        CancellationToken cancellationToken)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO sim_articles (simulation_id, code, designation, article_type, size, color, is_template, is_generated, procurement_type, lead_time_days, unit, is_simulated)
            OUTPUT INSERTED.id
            VALUES (@sim, @code, @des, @type, NULL, NULL, @template, 0, @proc, @lead, @unit, 1)
            """;
        cmd.Parameters.AddWithValue("@sim", simulationId);
        cmd.Parameters.AddWithValue("@code", code);
        cmd.Parameters.AddWithValue("@des", designation);
        cmd.Parameters.AddWithValue("@type", articleType);
        cmd.Parameters.AddWithValue("@template", isTemplate);
        cmd.Parameters.AddWithValue("@proc", procurementType);
        cmd.Parameters.AddWithValue("@lead", leadTimeDays);
        cmd.Parameters.AddWithValue("@unit", SimulationBomUnits.Normalize(unit));
        return (int)(await cmd.ExecuteScalarAsync(cancellationToken) ?? throw new InvalidOperationException());
    }

    private static async Task UpdateComponentUnitAsync(
        SqlConnection connection,
        int simulationId,
        int componentArticleId,
        string unit,
        CancellationToken cancellationToken,
        SqlTransaction? transaction = null)
    {
        await using var cmd = connection.CreateCommand();
        if (transaction is not null)
        {
            cmd.Transaction = transaction;
        }

        cmd.CommandText = """
            UPDATE sim_articles
            SET unit = @unit
            WHERE id = @id AND simulation_id = @sim AND is_generated = 0
            """;
        cmd.Parameters.AddWithValue("@unit", SimulationBomUnits.Normalize(unit));
        cmd.Parameters.AddWithValue("@id", componentArticleId);
        cmd.Parameters.AddWithValue("@sim", simulationId);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task DeleteGeneratedBomAndRoutingAsync(
        SqlConnection connection,
        int simulationId,
        int articleId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            DELETE FROM sim_bom_lines WHERE simulation_id = @sim AND parent_article_id = @art AND is_generated = 1;
            DELETE FROM sim_routing_operations WHERE simulation_id = @sim AND article_id = @art AND is_generated = 1;
            """;
        command.Parameters.AddWithValue("@sim", simulationId);
        command.Parameters.AddWithValue("@art", articleId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertDefaultCbnParameterAsync(
        SqlConnection connection,
        int simulationId,
        int articleId,
        int leadTimeDays,
        CancellationToken cancellationToken)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            IF NOT EXISTS (SELECT 1 FROM sim_cbn_parameters WHERE simulation_id = @sim AND article_id = @art)
            INSERT INTO sim_cbn_parameters (simulation_id, article_id, on_hand_stock, safety_stock, reserved_quantity, scheduled_receipt_production, scheduled_receipt_purchase, lead_time_days, lot_rule, min_lot, multiple_lot, is_simulated)
            VALUES (@sim, @art, 0, 0, 0, 0, 0, @lead, 'LotForLot', 0, 1, 1)
            """;
        cmd.Parameters.AddWithValue("@sim", simulationId);
        cmd.Parameters.AddWithValue("@art", articleId);
        cmd.Parameters.AddWithValue("@lead", leadTimeDays);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<List<SimulationDuplicationEngine.TemplateBomLine>> LoadTemplateBomAsync(
        SqlConnection connection, int simulationId, int templateId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT bl.parent_article_id, bl.component_article_id, c.code, bl.quantity_per, bl.scrap_rate, bl.offset_days,
                   bl.apply_size_coefficient, bl.apply_color_substitution
            FROM sim_bom_lines bl
            JOIN sim_articles c ON c.id = bl.component_article_id
            WHERE bl.simulation_id = @sim AND bl.parent_article_id = @parent AND bl.nomenclature_type = @nomType
            """;
        command.Parameters.AddWithValue("@sim", simulationId);
        command.Parameters.AddWithValue("@parent", templateId);
        command.Parameters.AddWithValue("@nomType", SimulationNomenclatureTypes.Base);
        var list = new List<SimulationDuplicationEngine.TemplateBomLine>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new SimulationDuplicationEngine.TemplateBomLine(
                reader.GetInt32(0),
                reader.GetInt32(1),
                reader.GetString(2),
                reader.GetDouble(3),
                reader.GetDouble(4),
                reader.GetInt32(5),
                reader.GetBoolean(6),
                reader.GetBoolean(7)));
        }

        return list;
    }

    private static async Task<List<SimulationDuplicationEngine.TemplateRoutingOp>> LoadTemplateRoutingAsync(
        SqlConnection connection, int simulationId, int templateId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT article_id, operation_number, operation_name, work_center, setup_time_minutes, run_time_minutes,
                   queue_time_minutes, move_time_minutes, apply_size_coefficient, apply_color_coefficient
            FROM sim_routing_operations
            WHERE simulation_id = @sim AND article_id = @art
            ORDER BY operation_number
            """;
        command.Parameters.AddWithValue("@sim", simulationId);
        command.Parameters.AddWithValue("@art", templateId);
        var list = new List<SimulationDuplicationEngine.TemplateRoutingOp>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new SimulationDuplicationEngine.TemplateRoutingOp(
                reader.GetInt32(0),
                reader.GetInt32(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.GetDouble(4),
                reader.GetDouble(5),
                reader.GetDouble(6),
                reader.GetDouble(7),
                reader.GetBoolean(8),
                reader.GetBoolean(9)));
        }

        return list;
    }

    private static async Task<List<SimulationDuplicationEngine.SubstitutionRule>> LoadSubstitutionsAsync(
        SqlConnection connection, int simulationId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT base_component_id, attribute_name, attribute_value, substitute_component_id
            FROM sim_component_substitution_rules WHERE simulation_id = @sim
            """;
        command.Parameters.AddWithValue("@sim", simulationId);
        var list = new List<SimulationDuplicationEngine.SubstitutionRule>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new SimulationDuplicationEngine.SubstitutionRule(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetInt32(3)));
        }

        return list;
    }

    private static async Task<int> InsertGeneratedArticleAsync(
        SqlConnection connection,
        int simulationId,
        int templateId,
        SimulationDuplicationEngine.GeneratedVariantArtifact artifact,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO sim_articles (simulation_id, base_article_id, code, designation, article_type, size, color, is_template, is_generated, procurement_type, lead_time_days, unit, is_simulated)
            OUTPUT INSERTED.id
            VALUES (@sim, @base, @code, @des, 'FinishedGood', @size, @color, 0, 1, 'Manufactured', 5, 'PCS', 1)
            """;
        command.Parameters.AddWithValue("@sim", simulationId);
        command.Parameters.AddWithValue("@base", templateId);
        command.Parameters.AddWithValue("@code", artifact.Code);
        command.Parameters.AddWithValue("@des", artifact.Designation);
        command.Parameters.AddWithValue("@size", artifact.Size);
        command.Parameters.AddWithValue("@color", artifact.Color);
        return (int)(await command.ExecuteScalarAsync(cancellationToken) ?? throw new InvalidOperationException());
    }

    private static async Task InsertGeneratedBomAsync(
        SqlConnection connection,
        int simulationId,
        int variantId,
        SimulationDuplicationEngine.GeneratedVariantArtifact artifact,
        IReadOnlyDictionary<int, string> codesById,
        CancellationToken cancellationToken)
    {
        var idByCode = codesById.ToDictionary(kv => kv.Value, kv => kv.Key, StringComparer.OrdinalIgnoreCase);
        foreach (var line in artifact.BomLines)
        {
            var componentId = line.ResolvedComponentId;
            if (!codesById.ContainsKey(componentId) && idByCode.TryGetValue(line.ComponentCode, out var fallback))
            {
                componentId = fallback;
            }

            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO sim_bom_lines (simulation_id, parent_article_id, component_article_id, quantity_per, scrap_rate, offset_days, apply_size_coefficient, apply_color_substitution, is_generated, is_simulated, nomenclature_type, alternative)
                VALUES (@sim, @parent, @comp, @qty, @scrap, @offset, @size, @color, 1, 1, @nomType, 0)
                """;
            command.Parameters.AddWithValue("@sim", simulationId);
            command.Parameters.AddWithValue("@parent", variantId);
            command.Parameters.AddWithValue("@comp", componentId);
            command.Parameters.AddWithValue("@qty", line.QuantityPer);
            command.Parameters.AddWithValue("@scrap", line.ScrapRate);
            command.Parameters.AddWithValue("@offset", line.OffsetDays);
            command.Parameters.AddWithValue("@size", line.ApplySizeCoefficient);
            command.Parameters.AddWithValue("@color", line.ApplyColorSubstitution);
            command.Parameters.AddWithValue("@nomType", SimulationNomenclatureTypes.Base);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task InsertGeneratedRoutingAsync(
        SqlConnection connection,
        int simulationId,
        int variantId,
        SimulationDuplicationEngine.GeneratedVariantArtifact artifact,
        CancellationToken cancellationToken)
    {
        foreach (var op in artifact.RoutingOps)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO sim_routing_operations (simulation_id, article_id, operation_number, operation_name, work_center, setup_time_minutes, run_time_minutes, queue_time_minutes, move_time_minutes, apply_size_coefficient, apply_color_coefficient, is_generated, is_simulated)
                VALUES (@sim, @art, @no, @name, @wc, @setup, @run, @queue, @move, @size, @color, 1, 1)
                """;
            command.Parameters.AddWithValue("@sim", simulationId);
            command.Parameters.AddWithValue("@art", variantId);
            command.Parameters.AddWithValue("@no", op.OperationNumber);
            command.Parameters.AddWithValue("@name", op.OperationName);
            command.Parameters.AddWithValue("@wc", (object?)op.WorkCenter ?? DBNull.Value);
            command.Parameters.AddWithValue("@setup", op.SetupTimeMinutes);
            command.Parameters.AddWithValue("@run", op.RunTimeMinutes);
            command.Parameters.AddWithValue("@queue", op.QueueTimeMinutes);
            command.Parameters.AddWithValue("@move", op.MoveTimeMinutes);
            command.Parameters.AddWithValue("@size", op.ApplySizeCoefficient);
            command.Parameters.AddWithValue("@color", op.ApplyColorCoefficient);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task EnsureGeneratedCbnParamsAsync(
        SqlConnection connection,
        int simulationId,
        int articleId,
        int leadTime,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            IF NOT EXISTS (SELECT 1 FROM sim_cbn_parameters WHERE simulation_id = @sim AND article_id = @art)
            INSERT INTO sim_cbn_parameters (simulation_id, article_id, on_hand_stock, safety_stock, reserved_quantity, scheduled_receipt_production, scheduled_receipt_purchase, lead_time_days, lot_rule, min_lot, multiple_lot, is_simulated)
            VALUES (@sim, @art, 0, 0, 0, 0, 0, @lead, 'LotForLot', 0, 1, 1)
            """;
        command.Parameters.AddWithValue("@sim", simulationId);
        command.Parameters.AddWithValue("@art", articleId);
        command.Parameters.AddWithValue("@lead", leadTime);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertVariantDefinitionAsync(
        SqlConnection connection,
        int simulationId,
        int templateId,
        SimulationDuplicationEngine.GeneratedVariantArtifact artifact,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO sim_variant_definitions (simulation_id, template_article_id, size, color, size_coefficient, color_coefficient, generated_article_code)
            VALUES (@sim, @template, @size, @color, @sizeCoef, @colorCoef, @code)
            """;
        command.Parameters.AddWithValue("@sim", simulationId);
        command.Parameters.AddWithValue("@template", templateId);
        command.Parameters.AddWithValue("@size", artifact.Size);
        command.Parameters.AddWithValue("@color", artifact.Color);
        command.Parameters.AddWithValue("@sizeCoef", artifact.SizeCoefficient);
        command.Parameters.AddWithValue("@colorCoef", artifact.ColorCoefficient);
        command.Parameters.AddWithValue("@code", artifact.Code);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private SqlConnection OpenConnection()
    {
        var connectionString = options.Value.ConnectionString;
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("Database:ConnectionString manquant.");
        }

        var connection = new SqlConnection(connectionString);
        connection.Open();
        return connection;
    }
}
