using Axioplan.GammesNomenclatures.Application.Abstractions;
using Axioplan.GammesNomenclatures.Application.Models.Commandes;
using Axioplan.GammesNomenclatures.Infrastructure.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace Axioplan.GammesNomenclatures.Infrastructure.Repositories;

public sealed class SqlServerCommandeRepository(IOptions<DatabaseOptions> options) : ICommandeRepository
{
    public async Task EnsureSchemaAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            IF COL_LENGTH('dbo.sales_orders', 'entered_by') IS NULL
                ALTER TABLE sales_orders ADD entered_by NVARCHAR(100) NULL;
            IF COL_LENGTH('dbo.sales_orders', 'shipping_address') IS NULL
                ALTER TABLE sales_orders ADD shipping_address NVARCHAR(500) NULL;
            IF COL_LENGTH('dbo.sales_orders', 'billing_address') IS NULL
                ALTER TABLE sales_orders ADD billing_address NVARCHAR(500) NULL;
            IF COL_LENGTH('dbo.sales_orders', 'delivery_mode') IS NULL
                ALTER TABLE sales_orders ADD delivery_mode NVARCHAR(100) NULL;
            IF COL_LENGTH('dbo.sales_orders', 'availability_date') IS NULL
                ALTER TABLE sales_orders ADD availability_date DATE NULL;
            IF COL_LENGTH('dbo.sales_orders', 'modified_at') IS NULL
                ALTER TABLE sales_orders ADD modified_at DATETIME2 NULL;
            IF COL_LENGTH('dbo.sales_orders', 'notes') IS NULL
                ALTER TABLE sales_orders ADD notes NVARCHAR(MAX) NULL;

            IF COL_LENGTH('dbo.sales_order_lines', 'model') IS NULL
                ALTER TABLE sales_order_lines ADD model NVARCHAR(100) NULL;
            IF COL_LENGTH('dbo.sales_order_lines', 'description') IS NULL
                ALTER TABLE sales_order_lines ADD description NVARCHAR(255) NULL;
            IF COL_LENGTH('dbo.sales_order_lines', 'supplier_ref') IS NULL
                ALTER TABLE sales_order_lines ADD supplier_ref NVARCHAR(100) NULL;
            IF COL_LENGTH('dbo.sales_order_lines', 'color_code') IS NULL
                ALTER TABLE sales_order_lines ADD color_code NVARCHAR(50) NULL;
            IF COL_LENGTH('dbo.sales_order_lines', 'color_label') IS NULL
                ALTER TABLE sales_order_lines ADD color_label NVARCHAR(100) NULL;
            IF COL_LENGTH('dbo.sales_order_lines', 'size_label') IS NULL
                ALTER TABLE sales_order_lines ADD size_label NVARCHAR(50) NULL;
            IF COL_LENGTH('dbo.sales_order_lines', 'unit_price') IS NULL
                ALTER TABLE sales_order_lines ADD unit_price FLOAT NULL;
            IF COL_LENGTH('dbo.sales_order_lines', 'line_amount') IS NULL
                ALTER TABLE sales_order_lines ADD line_amount FLOAT NULL;

            IF OBJECT_ID(N'dbo.sales_order_events', N'U') IS NULL
            BEGIN
                CREATE TABLE sales_order_events (
                    id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    sales_order_id INT NOT NULL,
                    so_code NVARCHAR(100) NOT NULL,
                    event_type NVARCHAR(30) NOT NULL,
                    customer_code NVARCHAR(50) NULL,
                    entered_by NVARCHAR(100) NULL,
                    created_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
                    FOREIGN KEY (sales_order_id) REFERENCES sales_orders(id)
                );
                CREATE INDEX IX_sales_order_events_created ON sales_order_events(created_at DESC);
            END;

            IF OBJECT_ID(N'dbo.ui_preferences', N'U') IS NULL
            BEGIN
                CREATE TABLE ui_preferences (
                    preference_key NVARCHAR(100) NOT NULL PRIMARY KEY,
                    preference_value NVARCHAR(255) NOT NULL,
                    updated_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
                );
            END;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<CommandeListItem>> GetCommandesAsync(CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);
        await using var connection = OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT so.id, so.code, so.customer_code, c.label, so.created_at, so.entered_by, so.status,
                   (SELECT COUNT(*) FROM sales_order_lines sol WHERE sol.sales_order_id = so.id)
            FROM sales_orders so
            LEFT JOIN customers c ON c.code = so.customer_code
            ORDER BY so.created_at DESC, so.code DESC
            """;

        var items = new List<CommandeListItem>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new CommandeListItem(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.GetDateTime(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.GetString(6),
                reader.GetInt32(7)));
        }

        return items;
    }

    public async Task<CommandeDetailDto?> GetCommandeAsync(int id, CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);
        await using var connection = OpenConnection();
        return await LoadDetailAsync(connection, id, null, cancellationToken);
    }

    public async Task<CommandeDetailDto?> GetCommandeBySoAsync(string soCode, CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);
        await using var connection = OpenConnection();
        return await LoadDetailAsync(connection, null, soCode, cancellationToken);
    }

    public async Task<string> PeekNextSoCodeAsync(CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);
        await using var connection = OpenConnection();
        return await GenerateNextSoCodeAsync(connection, cancellationToken);
    }

    public async Task<CommandeDetailDto> CreateCommandeAsync(
        CreateCommandeRequest request,
        CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);
        await using var connection = OpenConnection();
        await using var tx = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            var soCode = await GenerateNextSoCodeAsync(connection, cancellationToken, tx);
            await using var insert = connection.CreateCommand();
            insert.Transaction = tx;
            insert.CommandText = """
                INSERT INTO sales_orders (
                    code, customer_code, label, status, order_date, created_at, modified_at,
                    entered_by, shipping_address, billing_address, delivery_mode, availability_date, notes)
                OUTPUT INSERTED.id
                VALUES (
                    @code, @customer, @label, 'OPEN', CAST(SYSUTCDATETIME() AS DATE), SYSUTCDATETIME(), SYSUTCDATETIME(),
                    @enteredBy, @ship, @bill, @delivery, @avail, @notes)
                """;
            insert.Parameters.AddWithValue("@code", soCode);
            insert.Parameters.AddWithValue("@customer", (object?)request.CustomerCode ?? DBNull.Value);
            insert.Parameters.AddWithValue("@label", $"Commande {soCode}");
            insert.Parameters.AddWithValue("@enteredBy", (object?)NullIfEmpty(request.EnteredBy) ?? DBNull.Value);
            insert.Parameters.AddWithValue("@ship", (object?)NullIfEmpty(request.ShippingAddress) ?? DBNull.Value);
            insert.Parameters.AddWithValue("@bill", (object?)NullIfEmpty(request.BillingAddress) ?? DBNull.Value);
            insert.Parameters.AddWithValue("@delivery", (object?)NullIfEmpty(request.DeliveryMode) ?? DBNull.Value);
            insert.Parameters.AddWithValue("@avail", (object?)request.AvailabilityDate?.Date ?? DBNull.Value);
            insert.Parameters.AddWithValue("@notes", (object?)NullIfEmpty(request.Notes) ?? DBNull.Value);

            var orderId = Convert.ToInt32(await insert.ExecuteScalarAsync(cancellationToken));
            await ReplaceLinesAsync(connection, tx, orderId, request.Lines, cancellationToken);
            await InsertEventAsync(connection, tx, orderId, soCode, "CREATED", request.CustomerCode, request.EnteredBy, cancellationToken);
            await tx.CommitAsync(cancellationToken);

            return (await LoadDetailAsync(connection, orderId, null, cancellationToken))!;
        }
        catch
        {
            await tx.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<CommandeDetailDto> UpdateCommandeAsync(
        UpdateCommandeRequest request,
        CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);
        await using var connection = OpenConnection();
        await using var tx = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            await using var update = connection.CreateCommand();
            update.Transaction = tx;
            update.CommandText = """
                UPDATE sales_orders
                SET customer_code = @customer,
                    entered_by = @enteredBy,
                    shipping_address = @ship,
                    billing_address = @bill,
                    delivery_mode = @delivery,
                    availability_date = @avail,
                    notes = @notes,
                    status = @status,
                    modified_at = SYSUTCDATETIME()
                WHERE id = @id
                """;
            update.Parameters.AddWithValue("@id", request.Id);
            update.Parameters.AddWithValue("@customer", (object?)request.CustomerCode ?? DBNull.Value);
            update.Parameters.AddWithValue("@enteredBy", (object?)NullIfEmpty(request.EnteredBy) ?? DBNull.Value);
            update.Parameters.AddWithValue("@ship", (object?)NullIfEmpty(request.ShippingAddress) ?? DBNull.Value);
            update.Parameters.AddWithValue("@bill", (object?)NullIfEmpty(request.BillingAddress) ?? DBNull.Value);
            update.Parameters.AddWithValue("@delivery", (object?)NullIfEmpty(request.DeliveryMode) ?? DBNull.Value);
            update.Parameters.AddWithValue("@avail", (object?)request.AvailabilityDate?.Date ?? DBNull.Value);
            update.Parameters.AddWithValue("@notes", (object?)NullIfEmpty(request.Notes) ?? DBNull.Value);
            update.Parameters.AddWithValue("@status", string.IsNullOrWhiteSpace(request.Status) ? "OPEN" : request.Status);
            await update.ExecuteNonQueryAsync(cancellationToken);

            await ReplaceLinesAsync(connection, tx, request.Id, request.Lines, cancellationToken);

            await using var codeCmd = connection.CreateCommand();
            codeCmd.Transaction = tx;
            codeCmd.CommandText = "SELECT code FROM sales_orders WHERE id = @id";
            codeCmd.Parameters.AddWithValue("@id", request.Id);
            var soCode = (string)(await codeCmd.ExecuteScalarAsync(cancellationToken) ?? $"SO{request.Id}");

            await InsertEventAsync(connection, tx, request.Id, soCode, "UPDATED", request.CustomerCode, request.EnteredBy, cancellationToken);
            await tx.CommitAsync(cancellationToken);

            return (await LoadDetailAsync(connection, request.Id, null, cancellationToken))!;
        }
        catch
        {
            await tx.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<IReadOnlyList<CommandeHistoryItem>> GetHistoryAsync(CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);
        await using var connection = OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, sales_order_id, so_code, event_type, created_at, customer_code, entered_by
            FROM sales_order_events
            ORDER BY created_at DESC, id DESC
            """;

        var items = new List<CommandeHistoryItem>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new CommandeHistoryItem(
                reader.GetInt32(0),
                reader.GetInt32(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetDateTime(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6)));
        }

        return items;
    }

    public async Task<int> CountUnreadHistoryAsync(CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);
        await using var connection = OpenConnection();
        var lastVisit = await GetPreferenceAsync(connection, "commandes.history.last_visit", cancellationToken);

        await using var command = connection.CreateCommand();
        if (DateTime.TryParse(lastVisit, null, System.Globalization.DateTimeStyles.RoundtripKind, out var visitAt))
        {
            command.CommandText = """
                SELECT COUNT(*) FROM sales_order_events
                WHERE event_type = 'CREATED' AND created_at > @visit
                """;
            command.Parameters.AddWithValue("@visit", visitAt);
        }
        else
        {
            command.CommandText = "SELECT COUNT(*) FROM sales_order_events WHERE event_type = 'CREATED'";
        }

        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken) ?? 0);
    }

    public async Task MarkHistoryVisitedAsync(CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);
        await using var connection = OpenConnection();
        await SetPreferenceAsync(
            connection,
            "commandes.history.last_visit",
            DateTime.UtcNow.ToString("O"),
            cancellationToken);
    }

    public async Task<IReadOnlyList<ArticlePickItem>> SearchArticlesAsync(
        string? search,
        int take = 40,
        CancellationToken cancellationToken = default)
    {
        await using var connection = OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT TOP (@take) id, code, label, article_type
            FROM articles
            WHERE (@q = '' OR code LIKE @like OR label LIKE @like)
            ORDER BY
                CASE WHEN code LIKE 'PE%' THEN 0 ELSE 1 END,
                code
            """;
        var q = search?.Trim() ?? string.Empty;
        command.Parameters.AddWithValue("@take", take);
        command.Parameters.AddWithValue("@q", q);
        command.Parameters.AddWithValue("@like", $"%{q}%");

        var items = new List<ArticlePickItem>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new ArticlePickItem(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3)));
        }

        return items;
    }

    public async Task<IReadOnlyList<(string Code, string Label)>> GetCustomersAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = OpenConnection();
        await using var command = connection.CreateCommand();
        // Une seule requête : évite deux DataReader ouverts sur la même connexion.
        command.CommandText = """
            SELECT code, label FROM (
                SELECT code, label FROM customers
                UNION
                SELECT DISTINCT so.customer_code AS code, so.customer_code AS label
                FROM sales_orders so
                WHERE so.customer_code IS NOT NULL AND so.customer_code <> ''
                  AND NOT EXISTS (SELECT 1 FROM customers c WHERE c.code = so.customer_code)
            ) x
            ORDER BY label
            """;

        var items = new List<(string, string)>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add((reader.GetString(0), reader.GetString(1)));
        }

        return items;
    }

    private async Task<CommandeDetailDto?> LoadDetailAsync(
        SqlConnection connection,
        int? id,
        string? soCode,
        CancellationToken cancellationToken)
    {
        await using var header = connection.CreateCommand();
        header.CommandText = """
            SELECT so.id, so.code, so.customer_code, c.label, so.label, so.status, so.created_at, so.modified_at,
                   so.order_date, so.availability_date, so.entered_by, so.shipping_address, so.billing_address,
                   so.delivery_mode, so.notes
            FROM sales_orders so
            LEFT JOIN customers c ON c.code = so.customer_code
            WHERE (@id IS NOT NULL AND so.id = @id) OR (@so IS NOT NULL AND so.code = @so)
            """;
        header.Parameters.AddWithValue("@id", (object?)id ?? DBNull.Value);
        header.Parameters.AddWithValue("@so", (object?)soCode ?? DBNull.Value);

        int orderId;
        string code;
        string? customerCode;
        string? customerLabel;
        string? label;
        string status;
        DateTime createdAt;
        DateTime? modifiedAt;
        DateTime? orderDate;
        DateTime? availabilityDate;
        string? enteredBy;
        string? shipping;
        string? billing;
        string? delivery;
        string? notes;

        await using (var reader = await header.ExecuteReaderAsync(cancellationToken))
        {
            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }

            orderId = reader.GetInt32(0);
            code = reader.GetString(1);
            customerCode = reader.IsDBNull(2) ? null : reader.GetString(2);
            customerLabel = reader.IsDBNull(3) ? null : reader.GetString(3);
            label = reader.IsDBNull(4) ? null : reader.GetString(4);
            status = reader.GetString(5);
            createdAt = reader.GetDateTime(6);
            modifiedAt = reader.IsDBNull(7) ? null : reader.GetDateTime(7);
            orderDate = reader.IsDBNull(8) ? null : reader.GetDateTime(8);
            availabilityDate = reader.IsDBNull(9) ? null : reader.GetDateTime(9);
            enteredBy = reader.IsDBNull(10) ? null : reader.GetString(10);
            shipping = reader.IsDBNull(11) ? null : reader.GetString(11);
            billing = reader.IsDBNull(12) ? null : reader.GetString(12);
            delivery = reader.IsDBNull(13) ? null : reader.GetString(13);
            notes = reader.IsDBNull(14) ? null : reader.GetString(14);
        }

        var lines = new List<CommandeLineDto>();
        await using var linesCmd = connection.CreateCommand();
        linesCmd.CommandText = """
            SELECT sol.id, sol.line_no, sol.article_id, a.code, a.label,
                   sol.model, sol.description, sol.supplier_ref, sol.color_code, sol.color_label, sol.size_label,
                   sol.quantity, sol.unit, sol.unit_price, sol.line_amount
            FROM sales_order_lines sol
            JOIN articles a ON a.id = sol.article_id
            WHERE sol.sales_order_id = @id
            ORDER BY sol.line_no
            """;
        linesCmd.Parameters.AddWithValue("@id", orderId);
        await using var lineReader = await linesCmd.ExecuteReaderAsync(cancellationToken);
        while (await lineReader.ReadAsync(cancellationToken))
        {
            lines.Add(new CommandeLineDto(
                lineReader.GetInt32(0),
                lineReader.GetInt32(1),
                lineReader.GetInt32(2),
                lineReader.GetString(3),
                lineReader.GetString(4),
                lineReader.IsDBNull(5) ? null : lineReader.GetString(5),
                lineReader.IsDBNull(6) ? null : lineReader.GetString(6),
                lineReader.IsDBNull(7) ? null : lineReader.GetString(7),
                lineReader.IsDBNull(8) ? null : lineReader.GetString(8),
                lineReader.IsDBNull(9) ? null : lineReader.GetString(9),
                lineReader.IsDBNull(10) ? null : lineReader.GetString(10),
                lineReader.GetDouble(11),
                lineReader.GetString(12),
                lineReader.IsDBNull(13) ? null : lineReader.GetDouble(13),
                lineReader.IsDBNull(14) ? null : lineReader.GetDouble(14)));
        }

        return new CommandeDetailDto(
            orderId, code, customerCode, customerLabel, label, status, createdAt, modifiedAt,
            orderDate, availabilityDate, enteredBy, shipping, billing, delivery, notes, lines);
    }

    private static async Task ReplaceLinesAsync(
        SqlConnection connection,
        SqlTransaction tx,
        int orderId,
        IReadOnlyList<CommandeLineInput> lines,
        CancellationToken cancellationToken)
    {
        await using var delete = connection.CreateCommand();
        delete.Transaction = tx;
        delete.CommandText = "DELETE FROM sales_order_lines WHERE sales_order_id = @id";
        delete.Parameters.AddWithValue("@id", orderId);
        await delete.ExecuteNonQueryAsync(cancellationToken);

        var lineNo = 10;
        foreach (var line in lines)
        {
            var articleId = line.ArticleId;
            if (articleId is null or <= 0)
            {
                if (string.IsNullOrWhiteSpace(line.ArticleCode))
                {
                    continue;
                }

                await using var find = connection.CreateCommand();
                find.Transaction = tx;
                find.CommandText = "SELECT TOP 1 id FROM articles WHERE code = @code";
                find.Parameters.AddWithValue("@code", line.ArticleCode.Trim());
                var found = await find.ExecuteScalarAsync(cancellationToken);
                if (found is null)
                {
                    throw new InvalidOperationException($"Article introuvable : {line.ArticleCode}");
                }

                articleId = Convert.ToInt32(found);
            }

            var qty = line.Quantity <= 0 ? 1 : line.Quantity;
            double? amount = line.UnitPrice is null ? null : Math.Round(line.UnitPrice.Value * qty, 2);

            await using var insert = connection.CreateCommand();
            insert.Transaction = tx;
            insert.CommandText = """
                INSERT INTO sales_order_lines (
                    sales_order_id, line_no, article_id, quantity, unit,
                    model, description, supplier_ref, color_code, color_label, size_label,
                    unit_price, line_amount, external_ref)
                VALUES (
                    @orderId, @lineNo, @articleId, @qty, @unit,
                    @model, @desc, @supplier, @colorCode, @colorLabel, @size,
                    @price, @amount, @ext)
                """;
            insert.Parameters.AddWithValue("@orderId", orderId);
            insert.Parameters.AddWithValue("@lineNo", lineNo);
            insert.Parameters.AddWithValue("@articleId", articleId.Value);
            insert.Parameters.AddWithValue("@qty", qty);
            insert.Parameters.AddWithValue("@unit", string.IsNullOrWhiteSpace(line.Unit) ? "PIECE" : line.Unit);
            insert.Parameters.AddWithValue("@model", (object?)NullIfEmpty(line.Model) ?? DBNull.Value);
            insert.Parameters.AddWithValue("@desc", (object?)NullIfEmpty(line.Description) ?? DBNull.Value);
            insert.Parameters.AddWithValue("@supplier", (object?)NullIfEmpty(line.SupplierRef) ?? DBNull.Value);
            insert.Parameters.AddWithValue("@colorCode", (object?)NullIfEmpty(line.ColorCode) ?? DBNull.Value);
            insert.Parameters.AddWithValue("@colorLabel", (object?)NullIfEmpty(line.ColorLabel) ?? DBNull.Value);
            insert.Parameters.AddWithValue("@size", (object?)NullIfEmpty(line.SizeLabel) ?? DBNull.Value);
            insert.Parameters.AddWithValue("@price", (object?)line.UnitPrice ?? DBNull.Value);
            insert.Parameters.AddWithValue("@amount", (object?)amount ?? DBNull.Value);
            insert.Parameters.AddWithValue("@ext", (object?)NullIfEmpty(line.ArticleCode) ?? DBNull.Value);
            await insert.ExecuteNonQueryAsync(cancellationToken);
            lineNo += 10;
        }
    }

    private static async Task InsertEventAsync(
        SqlConnection connection,
        SqlTransaction tx,
        int orderId,
        string soCode,
        string eventType,
        string? customerCode,
        string? enteredBy,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = """
            INSERT INTO sales_order_events (sales_order_id, so_code, event_type, customer_code, entered_by)
            VALUES (@id, @so, @type, @customer, @by)
            """;
        command.Parameters.AddWithValue("@id", orderId);
        command.Parameters.AddWithValue("@so", soCode);
        command.Parameters.AddWithValue("@type", eventType);
        command.Parameters.AddWithValue("@customer", (object?)customerCode ?? DBNull.Value);
        command.Parameters.AddWithValue("@by", (object?)enteredBy ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<string> GenerateNextSoCodeAsync(
        SqlConnection connection,
        CancellationToken cancellationToken,
        SqlTransaction? tx = null)
    {
        var year = DateTime.UtcNow.ToString("yy");
        var prefix = $"SO{year}";

        await using var command = connection.CreateCommand();
        if (tx is not null)
        {
            command.Transaction = tx;
        }

        command.CommandText = """
            SELECT MAX(code) FROM sales_orders
            WHERE code LIKE @prefix + '[0-9][0-9][0-9][0-9][0-9][0-9]'
            """;
        command.Parameters.AddWithValue("@prefix", prefix);
        var max = await command.ExecuteScalarAsync(cancellationToken) as string;
        var next = 1;
        if (!string.IsNullOrWhiteSpace(max) && max.Length >= prefix.Length + 6
            && int.TryParse(max[prefix.Length..], out var current))
        {
            next = current + 1;
        }

        return $"{prefix}{next:D6}";
    }

    private static async Task<string?> GetPreferenceAsync(
        SqlConnection connection,
        string key,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT preference_value FROM ui_preferences WHERE preference_key = @key";
        command.Parameters.AddWithValue("@key", key);
        return await command.ExecuteScalarAsync(cancellationToken) as string;
    }

    private static async Task SetPreferenceAsync(
        SqlConnection connection,
        string key,
        string value,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            MERGE ui_preferences AS t
            USING (SELECT @key AS preference_key) AS s
            ON t.preference_key = s.preference_key
            WHEN MATCHED THEN UPDATE SET preference_value = @value, updated_at = SYSUTCDATETIME()
            WHEN NOT MATCHED THEN INSERT (preference_key, preference_value) VALUES (@key, @value);
            """;
        command.Parameters.AddWithValue("@key", key);
        command.Parameters.AddWithValue("@value", value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string? NullIfEmpty(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private SqlConnection OpenConnection()
    {
        var cs = options.Value.ConnectionString
            ?? throw new InvalidOperationException("Database:ConnectionString manquante.");
        var connection = new SqlConnection(cs);
        connection.Open();
        return connection;
    }
}
