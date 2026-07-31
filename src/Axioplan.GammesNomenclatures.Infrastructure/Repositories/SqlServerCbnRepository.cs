using Axioplan.GammesNomenclatures.Application.Abstractions;
using Axioplan.GammesNomenclatures.Application.Models;
using Axioplan.GammesNomenclatures.Domain;
using Axioplan.GammesNomenclatures.Infrastructure.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace Axioplan.GammesNomenclatures.Infrastructure.Repositories;

public sealed class SqlServerCbnRepository(IOptions<DatabaseOptions> options) : ICbnRepository
{
    private sealed record OrderLineContext(
        int LineId,
        int LineNo,
        string ArticleCode,
        string? SizeCode,
        string? ColorCode,
        double Quantity,
        string Unit,
        string? ExternalRef);

    private sealed record BomLineContext(
        int LineNo,
        int ParentArticleId,
        int ComponentArticleId,
        string ComponentCode,
        string ComponentLabel,
        string ComponentType,
        double QuantityBase,
        string Unit,
        double LossRate,
        string Behavior,
        int BomLevel,
        string SourcePath);

    public async Task<IReadOnlyList<SalesOrderListItem>> GetSalesOrdersAsync(
        string? dataSourceFilter = null,
        CancellationToken cancellationToken = default)
    {
        await using var connection = OpenConnection();
        await using var command = connection.CreateCommand();
            command.CommandText = """
            SELECT so.id, so.code, so.label, so.status, COUNT(sol.id) AS line_count
            FROM sales_orders so
            LEFT JOIN sales_order_lines sol ON sol.sales_order_id = so.id
            WHERE (
                @ds IS NULL
                OR so.data_source = @ds
                OR (so.data_source IS NULL AND @ds = N'DEMO')
                OR so.code = N'CMD-TUNIMAPULF'
            )
            GROUP BY so.id, so.code, so.label, so.status
            ORDER BY CASE WHEN so.code = N'CMD-TUNIMAPULF' THEN 0 ELSE 1 END, so.code
            """;
        command.Parameters.AddWithValue("@ds", (object?)dataSourceFilter ?? DBNull.Value);

        var items = new List<SalesOrderListItem>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new SalesOrderListItem(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.GetString(3),
                reader.GetInt32(4)));
        }

        return items;
    }

    public async Task<IReadOnlyList<SalesOrderLineItem>> GetSalesOrderLinesAsync(
        int salesOrderId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT sol.id, sol.line_no, a.code,
                   sz.technical_code, col.technical_code,
                   sol.quantity, sol.unit, sol.external_ref
            FROM sales_order_lines sol
            JOIN articles a ON a.id = sol.article_id
            LEFT JOIN attribute_options sz ON sz.id = sol.size_option_id
            LEFT JOIN attribute_options col ON col.id = sol.color_option_id
            WHERE sol.sales_order_id = @salesOrderId
            ORDER BY sol.line_no
            """;
        command.Parameters.AddWithValue("@salesOrderId", salesOrderId);

        var items = new List<SalesOrderLineItem>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new SalesOrderLineItem(
                reader.GetInt32(0),
                reader.GetInt32(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.GetDouble(5),
                reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetString(7)));
        }

        return items;
    }

    public async Task<IReadOnlyList<ImportedSalesOrderDetail>> GetImportedSalesOrdersDetailAsync(
        string? dataSourceFilter = null,
        CancellationToken cancellationToken = default)
    {
        await using var connection = OpenConnection();
        await EnsureSalesOrderLineExtraColumnsAsync(connection, cancellationToken);

        var headers = new List<(
            int Id, string Code, string? Label, string? CustomerCode, string Status,
            DateTime? OrderDate, string? DataSource, int? ImportBatchId,
            int LineCount, double TotalQty, string ArticlePreview)>();

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT
                    so.id,
                    so.code,
                    so.label,
                    so.customer_code,
                    so.status,
                    so.order_date,
                    so.data_source,
                    so.import_batch_id,
                    COUNT(sol.id) AS line_count,
                    ISNULL(SUM(sol.quantity), 0) AS total_qty,
                    ISNULL(STRING_AGG(CAST(a.code AS nvarchar(max)), N', ')
                        WITHIN GROUP (ORDER BY sol.line_no), N'') AS article_preview
                FROM sales_orders so
                LEFT JOIN sales_order_lines sol ON sol.sales_order_id = so.id
                LEFT JOIN articles a ON a.id = sol.article_id
                WHERE (
                    @ds IS NULL
                    OR so.data_source = @ds
                    OR (so.data_source IS NULL AND @ds = N'DEMO')
                    OR so.code = N'CMD-TUNIMAPULF'
                    OR EXISTS (
                        SELECT 1
                        FROM sales_order_lines x
                        INNER JOIN articles ax ON ax.id = x.article_id
                        WHERE x.sales_order_id = so.id
                          AND ax.code IN (N'TUNIMAPULF', N'TUNIMAPULF_BASE')
                    )
                )
                GROUP BY
                    so.id, so.code, so.label, so.customer_code, so.status,
                    so.order_date, so.data_source, so.import_batch_id
                ORDER BY CASE WHEN so.code = N'CMD-TUNIMAPULF' THEN 0 ELSE 1 END, so.code
                """;
            command.Parameters.AddWithValue("@ds", (object?)dataSourceFilter ?? DBNull.Value);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                headers.Add((
                    reader.GetInt32(0),
                    reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3),
                    reader.GetString(4),
                    reader.IsDBNull(5) ? null : reader.GetDateTime(5),
                    reader.IsDBNull(6) ? null : reader.GetString(6),
                    reader.IsDBNull(7) ? null : Convert.ToInt32(reader.GetValue(7)),
                    reader.GetInt32(8),
                    Convert.ToDouble(reader.GetValue(9)),
                    reader.IsDBNull(10) ? string.Empty : reader.GetString(10)));
            }
        }

        if (headers.Count == 0)
        {
            return [];
        }

        var linesByOrder = new Dictionary<int, List<ImportedSalesOrderLineDetail>>();
        foreach (var h in headers)
        {
            linesByOrder[h.Id] = [];
        }

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT
                    sol.sales_order_id,
                    sol.id,
                    sol.line_no,
                    a.code,
                    a.label,
                    sz.technical_code,
                    col.technical_code,
                    sol.quantity,
                    ISNULL(sol.qty_launched, 0),
                    ISNULL(sol.qty_produced, 0),
                    sol.last_operation,
                    sol.somme_operations,
                    sol.somme_op_70_commande,
                    sol.somme_op_70_article,
                    sol.unit,
                    sol.external_ref,
                    sol.remaining_to_launch,
                    sol.remaining_to_produce,
                    sol.delivery_date
                FROM sales_order_lines sol
                JOIN articles a ON a.id = sol.article_id
                LEFT JOIN attribute_options sz ON sz.id = sol.size_option_id
                LEFT JOIN attribute_options col ON col.id = sol.color_option_id
                WHERE sol.sales_order_id IN (
                    SELECT so.id FROM sales_orders so
                    WHERE (
                        @ds IS NULL
                        OR so.data_source = @ds
                        OR (so.data_source IS NULL AND @ds = N'DEMO')
                        OR so.code = N'CMD-TUNIMAPULF'
                        OR EXISTS (
                            SELECT 1
                            FROM sales_order_lines x
                            INNER JOIN articles ax ON ax.id = x.article_id
                            WHERE x.sales_order_id = so.id
                              AND ax.code IN (N'TUNIMAPULF', N'TUNIMAPULF_BASE')
                        )
                    )
                )
                ORDER BY sol.sales_order_id, sol.line_no
                """;
            command.Parameters.AddWithValue("@ds", (object?)dataSourceFilter ?? DBNull.Value);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var orderId = reader.GetInt32(0);
                if (!linesByOrder.TryGetValue(orderId, out var list))
                {
                    continue;
                }

                list.Add(ReadImportedLine(reader));
            }
        }

        return headers
            .Select(h => new ImportedSalesOrderDetail(
                h.Id,
                h.Code,
                h.Label,
                h.CustomerCode,
                h.Status,
                h.OrderDate,
                h.DataSource,
                h.ImportBatchId,
                h.LineCount,
                h.TotalQty,
                TruncatePreview(h.ArticlePreview, 120),
                linesByOrder.TryGetValue(h.Id, out var lines) ? lines : Array.Empty<ImportedSalesOrderLineDetail>()))
            .ToList();
    }

    public async Task EnsureTunimapulfSalesOrderAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = OpenConnection();
        await EnsureSalesOrderLineExtraColumnsAsync(connection, cancellationToken);

        var familyId = await ScalarIntAsync(connection, """
            SELECT TOP 1 id FROM article_families ORDER BY CASE WHEN code = N'PULL' THEN 0 ELSE 1 END, id
            """, cancellationToken) ?? await ScalarIntAsync(connection, "SELECT TOP 1 id FROM article_families", cancellationToken);

        if (familyId is null)
        {
            throw new InvalidOperationException("Aucune famille article disponible pour créer TUNIMAPULF.");
        }

        int articleId;
        await using (var find = connection.CreateCommand())
        {
            find.CommandText = "SELECT id FROM articles WHERE code = N'TUNIMAPULF'";
            var existing = await find.ExecuteScalarAsync(cancellationToken);
            if (existing is int id)
            {
                articleId = id;
            }
            else if (existing is not null && existing != DBNull.Value)
            {
                articleId = Convert.ToInt32(existing);
            }
            else
            {
                await using var insertArt = connection.CreateCommand();
                insertArt.CommandText = """
                    INSERT INTO articles (family_id, code, label, article_type, default_unit, status, source_system, creation_mode)
                    OUTPUT INSERTED.id
                    VALUES (@familyId, N'TUNIMAPULF', N'TUNIMAPULF (simulation)', N'FINISHED_GOOD', N'PIECE', N'ACTIVE', N'TUNIMAPULF_SEED', N'MANUAL')
                    """;
                insertArt.Parameters.AddWithValue("@familyId", familyId.Value);
                articleId = Convert.ToInt32(await insertArt.ExecuteScalarAsync(cancellationToken));
            }
        }

        int orderId;
        await using (var find = connection.CreateCommand())
        {
            find.CommandText = "SELECT id FROM sales_orders WHERE code = N'CMD-TUNIMAPULF'";
            var existing = await find.ExecuteScalarAsync(cancellationToken);
            if (existing is not null && existing != DBNull.Value)
            {
                orderId = Convert.ToInt32(existing);
                await using var upd = connection.CreateCommand();
                upd.CommandText = """
                    UPDATE sales_orders
                    SET customer_code = COALESCE(customer_code, N'TUNIMA'),
                        label = COALESCE(label, N'Commande TUNIMAPULF'),
                        status = N'OPEN',
                        data_source = COALESCE(data_source, N'DEMO')
                    WHERE id = @id
                    """;
                upd.Parameters.AddWithValue("@id", orderId);
                await upd.ExecuteNonQueryAsync(cancellationToken);
            }
            else
            {
                await using var insertSo = connection.CreateCommand();
                insertSo.CommandText = """
                    INSERT INTO sales_orders (code, customer_code, label, status, order_date, data_source)
                    OUTPUT INSERTED.id
                    VALUES (N'CMD-TUNIMAPULF', N'TUNIMA', N'Commande TUNIMAPULF', N'OPEN', CAST(GETDATE() AS date), N'DEMO')
                    """;
                orderId = Convert.ToInt32(await insertSo.ExecuteScalarAsync(cancellationToken));
            }
        }

        await using (var findLine = connection.CreateCommand())
        {
            findLine.CommandText = """
                SELECT id FROM sales_order_lines
                WHERE sales_order_id = @soId AND article_id = @articleId
                """;
            findLine.Parameters.AddWithValue("@soId", orderId);
            findLine.Parameters.AddWithValue("@articleId", articleId);
            var lineExisting = await findLine.ExecuteScalarAsync(cancellationToken);
            if (lineExisting is null || lineExisting == DBNull.Value)
            {
                var lineNo = await ScalarIntAsync(connection,
                    "SELECT ISNULL(MAX(line_no), 0) + 1 FROM sales_order_lines WHERE sales_order_id = @soId",
                    cancellationToken, ("@soId", orderId)) ?? 1;

                await using var insertLine = connection.CreateCommand();
                insertLine.CommandText = """
                    INSERT INTO sales_order_lines (
                        sales_order_id, line_no, article_id, quantity, unit, external_ref,
                        qty_launched, qty_produced, remaining_to_launch, remaining_to_produce, data_source)
                    VALUES (
                        @soId, @lineNo, @articleId, 100, N'PIECE', N'CMD-TUNIMAPULF',
                        0, 0, 100, 100, N'DEMO')
                    """;
                insertLine.Parameters.AddWithValue("@soId", orderId);
                insertLine.Parameters.AddWithValue("@lineNo", lineNo);
                insertLine.Parameters.AddWithValue("@articleId", articleId);
                await insertLine.ExecuteNonQueryAsync(cancellationToken);
            }
        }
    }

    public async Task<ImportedSalesOrderDetail> UpsertImportedSalesOrderLineAsync(
        UpsertImportedSalesOrderLineRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.OrderCode))
        {
            throw new InvalidOperationException("Le numéro de commande (Commande) est obligatoire.");
        }

        if (string.IsNullOrWhiteSpace(request.ArticleCode))
        {
            throw new InvalidOperationException("Le code article est obligatoire.");
        }

        await using var connection = OpenConnection();
        await EnsureSalesOrderLineExtraColumnsAsync(connection, cancellationToken);

        var dataSource = string.IsNullOrWhiteSpace(request.DataSource) ? "MONTEPULL_REAL" : request.DataSource!;
        var familyId = await ScalarIntAsync(connection, "SELECT TOP 1 id FROM article_families ORDER BY id", cancellationToken)
            ?? throw new InvalidOperationException("Aucune famille article disponible.");

        int articleId;
        await using (var find = connection.CreateCommand())
        {
            find.CommandText = "SELECT id FROM articles WHERE code = @code";
            find.Parameters.AddWithValue("@code", request.ArticleCode.Trim());
            var existing = await find.ExecuteScalarAsync(cancellationToken);
            if (existing is not null && existing != DBNull.Value)
            {
                articleId = Convert.ToInt32(existing);
                if (!string.IsNullOrWhiteSpace(request.Designation))
                {
                    await using var upd = connection.CreateCommand();
                    upd.CommandText = "UPDATE articles SET label = @label WHERE id = @id";
                    upd.Parameters.AddWithValue("@label", request.Designation);
                    upd.Parameters.AddWithValue("@id", articleId);
                    await upd.ExecuteNonQueryAsync(cancellationToken);
                }
            }
            else
            {
                await using var insertArt = connection.CreateCommand();
                insertArt.CommandText = """
                    INSERT INTO articles (family_id, code, label, article_type, default_unit, status, source_system, creation_mode)
                    OUTPUT INSERTED.id
                    VALUES (@familyId, @code, @label, N'FINISHED_GOOD', N'PIECE', N'ACTIVE', N'MANUAL_COMMANDE', N'MANUAL')
                    """;
                insertArt.Parameters.AddWithValue("@familyId", familyId);
                insertArt.Parameters.AddWithValue("@code", request.ArticleCode.Trim());
                insertArt.Parameters.AddWithValue("@label",
                    string.IsNullOrWhiteSpace(request.Designation) ? request.ArticleCode.Trim() : request.Designation!);
                articleId = Convert.ToInt32(await insertArt.ExecuteScalarAsync(cancellationToken));
            }
        }

        int orderId;
        await using (var find = connection.CreateCommand())
        {
            find.CommandText = "SELECT id FROM sales_orders WHERE code = @code";
            find.Parameters.AddWithValue("@code", request.OrderCode.Trim());
            var existing = await find.ExecuteScalarAsync(cancellationToken);
            if (existing is not null && existing != DBNull.Value)
            {
                orderId = Convert.ToInt32(existing);
                await using var upd = connection.CreateCommand();
                upd.CommandText = """
                    UPDATE sales_orders
                    SET customer_code = COALESCE(@customer, customer_code),
                        data_source = @ds,
                        status = N'OPEN'
                    WHERE id = @id
                    """;
                upd.Parameters.AddWithValue("@customer", (object?)request.CustomerCode ?? DBNull.Value);
                upd.Parameters.AddWithValue("@ds", dataSource);
                upd.Parameters.AddWithValue("@id", orderId);
                await upd.ExecuteNonQueryAsync(cancellationToken);
            }
            else
            {
                await using var insertSo = connection.CreateCommand();
                insertSo.CommandText = """
                    INSERT INTO sales_orders (code, customer_code, label, status, order_date, data_source)
                    OUTPUT INSERTED.id
                    VALUES (@code, @customer, @label, N'OPEN', CAST(GETDATE() AS date), @ds)
                    """;
                insertSo.Parameters.AddWithValue("@code", request.OrderCode.Trim());
                insertSo.Parameters.AddWithValue("@customer", (object?)request.CustomerCode ?? DBNull.Value);
                insertSo.Parameters.AddWithValue("@label", request.OrderCode.Trim());
                insertSo.Parameters.AddWithValue("@ds", dataSource);
                orderId = Convert.ToInt32(await insertSo.ExecuteScalarAsync(cancellationToken));
            }
        }

        var remainingLaunch = Math.Max(request.QtyOrdered - request.QtyLaunched, 0);
        var remainingProduce = Math.Max(request.QtyOrdered - request.QtyProduced, 0);

        await using (var findLine = connection.CreateCommand())
        {
            findLine.CommandText = """
                SELECT id FROM sales_order_lines
                WHERE sales_order_id = @soId AND article_id = @articleId
                """;
            findLine.Parameters.AddWithValue("@soId", orderId);
            findLine.Parameters.AddWithValue("@articleId", articleId);
            var lineExisting = await findLine.ExecuteScalarAsync(cancellationToken);
            if (lineExisting is not null && lineExisting != DBNull.Value)
            {
                await using var upd = connection.CreateCommand();
                upd.CommandText = """
                    UPDATE sales_order_lines SET
                        quantity = @qty,
                        qty_launched = @launched,
                        qty_produced = @produced,
                        last_operation = @lastOp,
                        somme_operations = @sommeOps,
                        somme_op_70_commande = @somme70c,
                        somme_op_70_article = @somme70a,
                        remaining_to_launch = @remLaunch,
                        remaining_to_produce = @remProd,
                        external_ref = @ext,
                        data_source = @ds
                    WHERE id = @id
                    """;
                upd.Parameters.AddWithValue("@qty", request.QtyOrdered);
                upd.Parameters.AddWithValue("@launched", request.QtyLaunched);
                upd.Parameters.AddWithValue("@produced", request.QtyProduced);
                upd.Parameters.AddWithValue("@lastOp", (object?)request.LastOperation ?? DBNull.Value);
                upd.Parameters.AddWithValue("@sommeOps", (object?)request.SommeOperations ?? DBNull.Value);
                upd.Parameters.AddWithValue("@somme70c", (object?)request.SommeOp70Commande ?? DBNull.Value);
                upd.Parameters.AddWithValue("@somme70a", (object?)request.SommeOp70Article ?? DBNull.Value);
                upd.Parameters.AddWithValue("@remLaunch", remainingLaunch);
                upd.Parameters.AddWithValue("@remProd", remainingProduce);
                upd.Parameters.AddWithValue("@ext", request.OrderCode.Trim());
                upd.Parameters.AddWithValue("@ds", dataSource);
                upd.Parameters.AddWithValue("@id", Convert.ToInt32(lineExisting));
                await upd.ExecuteNonQueryAsync(cancellationToken);
            }
            else
            {
                var lineNo = await ScalarIntAsync(connection,
                    "SELECT ISNULL(MAX(line_no), 0) + 1 FROM sales_order_lines WHERE sales_order_id = @soId",
                    cancellationToken, ("@soId", orderId)) ?? 1;

                await using var insertLine = connection.CreateCommand();
                insertLine.CommandText = """
                    INSERT INTO sales_order_lines (
                        sales_order_id, line_no, article_id, quantity, unit, external_ref,
                        qty_launched, qty_produced, last_operation,
                        somme_operations, somme_op_70_commande, somme_op_70_article,
                        remaining_to_launch, remaining_to_produce, data_source)
                    VALUES (
                        @soId, @lineNo, @articleId, @qty, N'PIECE', @ext,
                        @launched, @produced, @lastOp,
                        @sommeOps, @somme70c, @somme70a,
                        @remLaunch, @remProd, @ds)
                    """;
                insertLine.Parameters.AddWithValue("@soId", orderId);
                insertLine.Parameters.AddWithValue("@lineNo", lineNo);
                insertLine.Parameters.AddWithValue("@articleId", articleId);
                insertLine.Parameters.AddWithValue("@qty", request.QtyOrdered);
                insertLine.Parameters.AddWithValue("@ext", request.OrderCode.Trim());
                insertLine.Parameters.AddWithValue("@launched", request.QtyLaunched);
                insertLine.Parameters.AddWithValue("@produced", request.QtyProduced);
                insertLine.Parameters.AddWithValue("@lastOp", (object?)request.LastOperation ?? DBNull.Value);
                insertLine.Parameters.AddWithValue("@sommeOps", (object?)request.SommeOperations ?? DBNull.Value);
                insertLine.Parameters.AddWithValue("@somme70c", (object?)request.SommeOp70Commande ?? DBNull.Value);
                insertLine.Parameters.AddWithValue("@somme70a", (object?)request.SommeOp70Article ?? DBNull.Value);
                insertLine.Parameters.AddWithValue("@remLaunch", remainingLaunch);
                insertLine.Parameters.AddWithValue("@remProd", remainingProduce);
                insertLine.Parameters.AddWithValue("@ds", dataSource);
                await insertLine.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        var all = await GetImportedSalesOrdersDetailAsync(dataSource, cancellationToken);
        var created = all.FirstOrDefault(o => o.Id == orderId)
            ?? all.FirstOrDefault(o => string.Equals(o.Code, request.OrderCode.Trim(), StringComparison.OrdinalIgnoreCase));
        if (created is null)
        {
            // TUNIMAPULF / filtre : recharger sans filtre strict
            all = await GetImportedSalesOrdersDetailAsync(null, cancellationToken);
            created = all.First(o => o.Id == orderId);
        }

        return created;
    }

    private static ImportedSalesOrderLineDetail ReadImportedLine(SqlDataReader reader)
        => new(
            reader.GetInt32(1),
            reader.GetInt32(2),
            reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetString(5),
            reader.IsDBNull(6) ? null : reader.GetString(6),
            reader.GetDouble(7),
            reader.GetDouble(8),
            reader.GetDouble(9),
            reader.IsDBNull(10) ? null : reader.GetInt32(10),
            reader.IsDBNull(11) ? null : reader.GetDouble(11),
            reader.IsDBNull(12) ? null : reader.GetDouble(12),
            reader.IsDBNull(13) ? null : reader.GetDouble(13),
            reader.GetString(14),
            reader.IsDBNull(15) ? null : reader.GetString(15),
            reader.IsDBNull(16) ? null : reader.GetDouble(16),
            reader.IsDBNull(17) ? null : reader.GetDouble(17),
            reader.IsDBNull(18) ? null : reader.GetDateTime(18));

    private static async Task EnsureSalesOrderLineExtraColumnsAsync(
        SqlConnection connection,
        CancellationToken cancellationToken)
    {
        foreach (var sql in new[]
                 {
                     "IF COL_LENGTH('sales_order_lines', 'qty_launched') IS NULL ALTER TABLE sales_order_lines ADD qty_launched FLOAT NULL;",
                     "IF COL_LENGTH('sales_order_lines', 'qty_produced') IS NULL ALTER TABLE sales_order_lines ADD qty_produced FLOAT NULL;",
                     "IF COL_LENGTH('sales_order_lines', 'last_operation') IS NULL ALTER TABLE sales_order_lines ADD last_operation INT NULL;",
                     "IF COL_LENGTH('sales_order_lines', 'remaining_to_launch') IS NULL ALTER TABLE sales_order_lines ADD remaining_to_launch FLOAT NULL;",
                     "IF COL_LENGTH('sales_order_lines', 'remaining_to_produce') IS NULL ALTER TABLE sales_order_lines ADD remaining_to_produce FLOAT NULL;",
                     "IF COL_LENGTH('sales_order_lines', 'somme_operations') IS NULL ALTER TABLE sales_order_lines ADD somme_operations FLOAT NULL;",
                     "IF COL_LENGTH('sales_order_lines', 'somme_op_70_commande') IS NULL ALTER TABLE sales_order_lines ADD somme_op_70_commande FLOAT NULL;",
                     "IF COL_LENGTH('sales_order_lines', 'somme_op_70_article') IS NULL ALTER TABLE sales_order_lines ADD somme_op_70_article FLOAT NULL;",
                     "IF COL_LENGTH('sales_order_lines', 'data_source') IS NULL ALTER TABLE sales_order_lines ADD data_source NVARCHAR(40) NULL;",
                     "IF COL_LENGTH('sales_orders', 'data_source') IS NULL ALTER TABLE sales_orders ADD data_source NVARCHAR(40) NULL;",
                     "IF COL_LENGTH('sales_orders', 'import_batch_id') IS NULL ALTER TABLE sales_orders ADD import_batch_id BIGINT NULL;"
                 })
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = sql;
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task<int?> ScalarIntAsync(
        SqlConnection connection,
        string sql,
        CancellationToken cancellationToken,
        params (string Name, object Value)[] parameters)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            cmd.Parameters.AddWithValue(name, value);
        }

        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        if (result is null || result == DBNull.Value)
        {
            return null;
        }

        return Convert.ToInt32(result);
    }

    private static string TruncatePreview(string preview, int maxLen)
    {
        if (string.IsNullOrWhiteSpace(preview) || preview.Length <= maxLen)
        {
            return preview ?? string.Empty;
        }

        return preview[..(maxLen - 1)] + "…";
    }

    public async Task<CbnRunResult> RunCbnAsync(CbnRunRequest request, CancellationToken cancellationToken = default)
    {
        await using var connection = OpenConnection();

        var orderCode = await LoadOrderCodeAsync(connection, request.SalesOrderId, cancellationToken)
            ?? throw new InvalidOperationException($"Commande introuvable : {request.SalesOrderId}");

        var familyId = await LoadFamilyIdAsync(connection, request.ProductFamilyCode, cancellationToken)
            ?? throw new InvalidOperationException($"Famille introuvable : {request.ProductFamilyCode}");

        var cbnParams = await LoadCbnParametersAsync(connection, cancellationToken);
        await ValidateCbnPreconditionsAsync(connection, request.ProductFamilyCode, cbnParams, cancellationToken);

        var orderLines = await LoadOrderLinesForFamilyAsync(
            connection, request.SalesOrderId, familyId, cancellationToken);

        if (orderLines.Count == 0)
        {
            throw new InvalidOperationException(
                "Aucune ligne de commande pour cette famille. Verifiez la commande et la famille produit.");
        }

        var maxCascade = int.Parse(cbnParams.GetValueOrDefault("PEGGING_MAX_CASCADE_LEVEL", "5"));
        var parentArticleId = await LoadFinishedGoodArticleIdAsync(connection, request.ProductFamilyCode, cancellationToken);
        var bomLines = await LoadFlattenedBomLinesAsync(
            connection, request.ProductFamilyCode, parentArticleId, maxCascade, cancellationToken);

        if (bomLines.Count == 0)
        {
            throw new InvalidOperationException($"Nomenclature de base introuvable pour {request.ProductFamilyCode}.");
        }

        var runId = await InsertCbnRunAsync(connection, request.SalesOrderId, familyId, cancellationToken);
        var requirementFormula = await LoadRequirementFormulaAsync(connection, request.ProductFamilyCode, cancellationToken);

        var flattenedRows = new List<CbnFlattenedBomRow>();
        var requirements = new List<CbnRequirementRow>();
        var traces = new List<CbnTraceRow>();
        var requirementDrafts = new List<CbnEngine.MaterialRequirementDraft>();

        foreach (var bomLine in bomLines)
        {
            await InsertFlattenedBomLineAsync(connection, runId, bomLine, cancellationToken);
            flattenedRows.Add(new CbnFlattenedBomRow(
                bomLine.LineNo,
                bomLine.ComponentCode,
                bomLine.ComponentLabel,
                bomLine.ComponentType,
                ComputeCalculationMode(bomLine.Behavior),
                bomLine.BomLevel,
                bomLine.QuantityBase,
                bomLine.Unit,
                bomLine.LossRate,
                bomLine.Behavior,
                bomLine.SourcePath));
        }

        foreach (var line in orderLines)
        {
            var variantLabel = BuildVariantLabel(line);
            var sizeCoeff = line.SizeCode is not null
                ? await LoadCoefficientAsync(connection, request.ProductFamilyCode, "SIZE", line.SizeCode, cancellationToken)
                : 1.0;
            var colorCoeff = line.ColorCode is not null
                ? await LoadCoefficientAsync(connection, request.ProductFamilyCode, "COLOR", line.ColorCode, cancellationToken)
                : 1.0;
            var argumentCoeffs = await BuildArgumentCoefficientMapAsync(
                connection,
                request.ProductFamilyCode,
                line.SizeCode,
                line.ColorCode,
                cancellationToken);

            foreach (var bomLine in bomLines)
            {
                var engineLine = new CbnEngine.FlattenedBomLine(
                    bomLine.LineNo,
                    bomLine.ComponentCode,
                    bomLine.QuantityBase,
                    bomLine.Unit,
                    bomLine.LossRate,
                    bomLine.Behavior);

                var draft = CbnEngine.ComputeRequirement(
                    engineLine,
                    line.Quantity,
                    variantLabel,
                    sizeCoeff,
                    colorCoeff,
                    requirementFormula?.Expression,
                    requirementFormula?.ApplyOrderQuantity ?? true,
                    argumentCoeffs);

                requirementDrafts.Add(draft);

                await InsertRequirementAsync(connection, runId, line.LineId, bomLine, draft, cancellationToken);

                requirements.Add(new CbnRequirementRow(
                    variantLabel,
                    bomLine.ComponentCode,
                    bomLine.ComponentLabel,
                    bomLine.ComponentType,
                    draft.CalculationMode,
                    draft.QuantityNet,
                    draft.QuantityGross,
                    draft.Unit,
                    draft.SourcePath));

                await InsertTraceAsync(connection, runId, line.LineId, variantLabel, draft.Trace, cancellationToken);
                traces.Add(new CbnTraceRow(
                    variantLabel,
                    draft.Trace.ComponentCode,
                    draft.Trace.CalculationMode,
                    draft.Trace.RuleCode,
                    draft.Trace.BaseValue,
                    draft.Trace.OrderQuantity,
                    draft.Trace.SizeCoefficient,
                    draft.Trace.ColorCoefficient,
                    draft.Trace.LossRate,
                    draft.Trace.QuantityNet,
                    draft.Trace.QuantityGross,
                    draft.Trace.Unit,
                    draft.Trace.Formula));
            }
        }

        var componentTotals = CbnEngine.AggregateTotals(requirementDrafts)
            .Select(total =>
            {
                var label = bomLines.FirstOrDefault(b => b.ComponentCode == total.ComponentCode)?.ComponentLabel
                    ?? total.ComponentCode;
                var componentType = bomLines.FirstOrDefault(b => b.ComponentCode == total.ComponentCode)?.ComponentType
                    ?? "COMPONENT";
                return new CbnComponentTotal(total.ComponentCode, label, componentType, total.CalculationMode, total.TotalNet, total.TotalGross, total.Unit);
            })
            .ToList();

        return new CbnRunResult
        {
            RunId = runId,
            SalesOrderCode = orderCode,
            ProductFamilyCode = request.ProductFamilyCode,
            Status = "COMPLETED",
            FlattenedBom = flattenedRows,
            Requirements = requirements,
            ComponentTotals = componentTotals,
            Traces = traces,
        };
    }

    private SqlConnection OpenConnection()
    {
        var connectionString = options.Value.ConnectionString;
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Chaine de connexion SQL Server introuvable. Configurez Database:ConnectionString ou lancez : python scripts/init_db.py");
        }

        var connection = new SqlConnection(connectionString);
        connection.Open();
        return connection;
    }

    private static string BuildVariantLabel(OrderLineContext line)
    {
        var parts = new List<string> { line.ArticleCode };
        if (!string.IsNullOrWhiteSpace(line.SizeCode))
        {
            parts.Add(line.SizeCode);
        }

        if (!string.IsNullOrWhiteSpace(line.ColorCode))
        {
            parts.Add(line.ColorCode);
        }

        return string.Join("-", parts);
    }

    private static string ComputeCalculationMode(string behavior) => behavior switch
    {
        "CALCULATED" => "VARIABLE_MATERIAL",
        "COPY_TO_VALIDATE" => "TO_CONFIRM",
        _ => "FIXED_SUPPLY",
    };

    private static async Task<string?> LoadOrderCodeAsync(
        SqlConnection connection,
        int salesOrderId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT code FROM sales_orders WHERE id = @id";
        command.Parameters.AddWithValue("@id", salesOrderId);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result as string;
    }

    private static async Task<int?> LoadFamilyIdAsync(
        SqlConnection connection,
        string familyCode,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT id FROM product_families WHERE code = @code";
        command.Parameters.AddWithValue("@code", familyCode);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is int id ? id : null;
    }

    private static async Task<Dictionary<string, string>> LoadCbnParametersAsync(
        SqlConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT p.code, pv.value_text
            FROM mvp_parameters p
            JOIN mvp_parameter_groups g ON g.id = p.group_id
            LEFT JOIN mvp_parameter_values pv ON pv.parameter_id = p.id AND pv.scope_key IS NULL
            WHERE g.code IN ('CBN', 'PEGGING')
            """;

        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            parameters[reader.GetString(0)] = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
        }

        return parameters;
    }

    private static async Task ValidateCbnPreconditionsAsync(
        SqlConnection connection,
        string familyCode,
        Dictionary<string, string> cbnParams,
        CancellationToken cancellationToken)
    {
        var requireValidated = cbnParams.GetValueOrDefault("CBN_REQUIRE_VALIDATED_PROFILE", "true")
            .Equals("true", StringComparison.OrdinalIgnoreCase);

        if (requireValidated)
        {
            await using var profileCommand = connection.CreateCommand();
            profileCommand.CommandText = """
                SELECT TOP 1 gp.status
                FROM generation_profiles gp
                JOIN product_families pf ON pf.id = gp.product_family_id
                WHERE pf.code = @familyCode AND gp.can_generate_bom = 1
                ORDER BY gp.id
                """;
            profileCommand.Parameters.AddWithValue("@familyCode", familyCode);

            var profileStatus = await profileCommand.ExecuteScalarAsync(cancellationToken) as string;
            if (profileStatus is null || !GenerationEngine.ProfileCanGenerate(profileStatus))
            {
                throw new InvalidOperationException(
                    "Profil de generation non valide pour CBN. Parametre CBN_REQUIRE_VALIDATED_PROFILE actif.");
            }
        }

        var acceptedStatuses = cbnParams.GetValueOrDefault("CBN_ACCEPTED_BOM_STATUSES", "VALIDATED,GENERATED")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => s.ToUpperInvariant())
            .ToHashSet();

        await using var bomCommand = connection.CreateCommand();
        bomCommand.CommandText = """
            SELECT TOP 1 bb.status
            FROM bom_bases bb
            JOIN product_families pf ON pf.id = bb.product_family_id
            WHERE pf.code = @familyCode
            ORDER BY bb.version DESC
            """;
        bomCommand.Parameters.AddWithValue("@familyCode", familyCode);

        var bomStatus = (await bomCommand.ExecuteScalarAsync(cancellationToken) as string)?.ToUpperInvariant();
        if (bomStatus is null)
        {
            throw new InvalidOperationException($"Nomenclature de base introuvable pour {familyCode}.");
        }

        if (!acceptedStatuses.Contains(bomStatus))
        {
            throw new InvalidOperationException(
                $"Statut BOM '{bomStatus}' non accepte pour CBN. Acceptes : {string.Join(", ", acceptedStatuses)}. A confirmer.");
        }
    }

    private static async Task<List<OrderLineContext>> LoadOrderLinesForFamilyAsync(
        SqlConnection connection,
        int salesOrderId,
        int familyId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT sol.id, sol.line_no, a.code,
                   sz.technical_code, col.technical_code,
                   sol.quantity, sol.unit, sol.external_ref
            FROM sales_order_lines sol
            JOIN articles a ON a.id = sol.article_id
            LEFT JOIN attribute_options sz ON sz.id = sol.size_option_id
            LEFT JOIN attribute_options col ON col.id = sol.color_option_id
            WHERE sol.sales_order_id = @salesOrderId
              AND (sol.product_family_id = @familyId OR sol.product_family_id IS NULL)
            ORDER BY sol.line_no
            """;
        command.Parameters.AddWithValue("@salesOrderId", salesOrderId);
        command.Parameters.AddWithValue("@familyId", familyId);

        var lines = new List<OrderLineContext>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            lines.Add(new OrderLineContext(
                reader.GetInt32(0),
                reader.GetInt32(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.GetDouble(5),
                reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetString(7)));
        }

        return lines;
    }

    private static async Task<int> LoadFinishedGoodArticleIdAsync(
        SqlConnection connection,
        string familyCode,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT TOP 1 a.id
            FROM articles a
            JOIN article_families af ON af.id = a.family_id
            JOIN product_families pf ON pf.article_family_id = af.id
            WHERE pf.code = @familyCode AND a.article_type = 'FINISHED_GOOD'
            """;
        command.Parameters.AddWithValue("@familyCode", familyCode);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is int id ? id : throw new InvalidOperationException($"Article fini introuvable pour {familyCode}.");
    }

    private static async Task<List<BomLineContext>> LoadFlattenedBomLinesAsync(
        SqlConnection connection,
        string familyCode,
        int parentArticleId,
        int maxCascadeLevel,
        CancellationToken cancellationToken)
    {
        var rootLines = await LoadBomLinesForFamilyAsync(connection, familyCode, cancellationToken);
        var subBomCache = await LoadAllArticleSubBomsAsync(connection, cancellationToken);
        var flattened = BomFlattener.Flatten(
            rootLines,
            articleId => subBomCache.TryGetValue(articleId, out var lines) ? lines : null,
            maxCascadeLevel);

        return flattened.Select(line => new BomLineContext(
            line.LineNo,
            parentArticleId,
            line.ComponentArticleId,
            line.ComponentCode,
            line.ComponentLabel,
            line.ComponentType,
            line.QuantityPerUnit,
            line.Unit,
            line.LossRate,
            line.Behavior,
            line.BomLevel,
            line.SourcePath)).ToList();
    }

    private static async Task<Dictionary<int, IReadOnlyList<BomFlattener.BomLineInput>>> LoadAllArticleSubBomsAsync(
        SqlConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT aba.article_id, bl.line_no, a.id, a.code, a.label, a.article_type,
                   bl.quantity_base, bl.unit, bl.loss_rate, bl.behavior
            FROM article_bom_assignments aba
            JOIN bom_base_lines bl ON bl.bom_base_id = aba.bom_base_id
            JOIN articles a ON a.id = bl.component_article_id
            ORDER BY aba.article_id, bl.line_no
            """;

        var cache = new Dictionary<int, List<BomFlattener.BomLineInput>>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var articleId = reader.GetInt32(0);
            if (!cache.TryGetValue(articleId, out var lines))
            {
                lines = [];
                cache[articleId] = lines;
            }

            lines.Add(new BomFlattener.BomLineInput(
                reader.GetInt32(1),
                reader.GetInt32(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetDouble(6),
                reader.GetString(7),
                reader.GetDouble(8),
                reader.GetString(9)));
        }

        return cache.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<BomFlattener.BomLineInput>)pair.Value);
    }

    private static async Task<IReadOnlyList<BomFlattener.BomLineInput>> LoadBomLinesForFamilyAsync(
        SqlConnection connection,
        string familyCode,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT bl.line_no, a.id, a.code, a.label, a.article_type,
                   bl.quantity_base, bl.unit, bl.loss_rate, bl.behavior
            FROM bom_base_lines bl
            JOIN bom_bases b ON b.id = bl.bom_base_id
            JOIN product_families pf ON pf.id = b.product_family_id
            JOIN articles a ON a.id = bl.component_article_id
            WHERE pf.code = @familyCode
              AND b.id = (
                  SELECT TOP 1 b2.id
                  FROM bom_bases b2
                  WHERE b2.product_family_id = pf.id
                  ORDER BY b2.version DESC, b2.id DESC
              )
            ORDER BY bl.line_no
            """;
        command.Parameters.AddWithValue("@familyCode", familyCode);
        return await ReadBomLineInputsAsync(command, cancellationToken);
    }

    private static async Task<List<BomFlattener.BomLineInput>> ReadBomLineInputsAsync(
        SqlCommand command,
        CancellationToken cancellationToken)
    {
        var lines = new List<BomFlattener.BomLineInput>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            lines.Add(new BomFlattener.BomLineInput(
                reader.GetInt32(0),
                reader.GetInt32(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetDouble(5),
                reader.GetString(6),
                reader.GetDouble(7),
                reader.GetString(8)));
        }

        return lines;
    }

    private static async Task<int> InsertCbnRunAsync(
        SqlConnection connection,
        int salesOrderId,
        int familyId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO cbn_runs (sales_order_id, product_family_id, status)
            OUTPUT INSERTED.id
            VALUES (@salesOrderId, @familyId, 'COMPLETED')
            """;
        command.Parameters.AddWithValue("@salesOrderId", salesOrderId);
        command.Parameters.AddWithValue("@familyId", familyId);

        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static async Task InsertFlattenedBomLineAsync(
        SqlConnection connection,
        int runId,
        BomLineContext bomLine,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO cbn_flattened_bom_lines (
                cbn_run_id, parent_article_id, component_article_id, bom_line_no, bom_level,
                quantity_per_unit, unit, loss_rate, behavior, source_path)
            VALUES (@runId, @parentId, @componentId, @lineNo, @level, @qty, @unit, @loss, @behavior, @path)
            """;
        command.Parameters.AddWithValue("@runId", runId);
        command.Parameters.AddWithValue("@parentId", bomLine.ParentArticleId);
        command.Parameters.AddWithValue("@componentId", bomLine.ComponentArticleId);
        command.Parameters.AddWithValue("@lineNo", bomLine.LineNo);
        command.Parameters.AddWithValue("@level", bomLine.BomLevel);
        command.Parameters.AddWithValue("@qty", bomLine.QuantityBase);
        command.Parameters.AddWithValue("@unit", bomLine.Unit);
        command.Parameters.AddWithValue("@loss", bomLine.LossRate);
        command.Parameters.AddWithValue("@behavior", bomLine.Behavior);
        command.Parameters.AddWithValue("@path", bomLine.SourcePath);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<int> InsertRequirementAsync(
        SqlConnection connection,
        int runId,
        int lineId,
        BomLineContext bomLine,
        CbnEngine.MaterialRequirementDraft draft,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO cbn_material_requirements (
                cbn_run_id, sales_order_line_id, component_article_id, variant_label,
                quantity_net, quantity_gross, unit, source_path)
            OUTPUT INSERTED.id
            VALUES (@runId, @lineId, @componentId, @variant, @net, @gross, @unit, @path)
            """;
        command.Parameters.AddWithValue("@runId", runId);
        command.Parameters.AddWithValue("@lineId", lineId);
        command.Parameters.AddWithValue("@componentId", bomLine.ComponentArticleId);
        command.Parameters.AddWithValue("@variant", draft.VariantLabel);
        command.Parameters.AddWithValue("@net", draft.QuantityNet);
        command.Parameters.AddWithValue("@gross", draft.QuantityGross);
        command.Parameters.AddWithValue("@unit", draft.Unit);
        command.Parameters.AddWithValue("@path", draft.SourcePath);

        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static async Task InsertTraceAsync(
        SqlConnection connection,
        int runId,
        int lineId,
        string variantLabel,
        CbnEngine.CalculationTraceDraft trace,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO cbn_calculation_traces (
                cbn_run_id, sales_order_line_id, object_type, component_code, rule_code,
                base_value, order_quantity, size_coefficient, color_coefficient, loss_rate,
                quantity_net, quantity_gross, unit, formula)
            VALUES (
                @runId, @lineId, @objectType, @component, @rule,
                @base, @orderQty, @sizeCoeff, @colorCoeff, @loss,
                @net, @gross, @unit, @formula)
            """;
        command.Parameters.AddWithValue("@runId", runId);
        command.Parameters.AddWithValue("@lineId", lineId);
        command.Parameters.AddWithValue("@objectType", trace.ObjectType);
        command.Parameters.AddWithValue("@component", trace.ComponentCode);
        command.Parameters.AddWithValue("@rule", trace.RuleCode);
        command.Parameters.AddWithValue("@base", trace.BaseValue);
        command.Parameters.AddWithValue("@orderQty", trace.OrderQuantity);
        command.Parameters.AddWithValue("@sizeCoeff", (object?)trace.SizeCoefficient ?? DBNull.Value);
        command.Parameters.AddWithValue("@colorCoeff", (object?)trace.ColorCoefficient ?? DBNull.Value);
        command.Parameters.AddWithValue("@loss", trace.LossRate);
        command.Parameters.AddWithValue("@net", trace.QuantityNet);
        command.Parameters.AddWithValue("@gross", trace.QuantityGross);
        command.Parameters.AddWithValue("@unit", trace.Unit);
        command.Parameters.AddWithValue("@formula", trace.Formula);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<double> LoadCoefficientAsync(
        SqlConnection connection,
        string familyCode,
        string attributeCode,
        string optionCode,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT cc.coefficient
            FROM consumption_coefficients cc
            JOIN product_families pf ON pf.id = cc.product_family_id
            JOIN attribute_definitions ad ON ad.id = cc.attribute_id
            JOIN attribute_options ao ON ao.id = cc.option_id
            WHERE pf.code = @familyCode
              AND ad.code = @attributeCode
              AND ao.technical_code = @optionCode
              AND cc.status = 'VALIDATED'
            """;
        command.Parameters.AddWithValue("@familyCode", familyCode);
        command.Parameters.AddWithValue("@attributeCode", attributeCode);
        command.Parameters.AddWithValue("@optionCode", optionCode);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is double coefficient ? coefficient : Convert.ToDouble(result ?? 1.0);
    }

    private sealed record RequirementFormulaRow(string Expression, bool ApplyOrderQuantity);

    private static async Task<RequirementFormulaRow?> LoadRequirementFormulaAsync(
        SqlConnection connection,
        string familyCode,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT rf.expression, rf.apply_order_quantity
            FROM requirement_formulas rf
            JOIN product_families pf ON pf.id = rf.product_family_id
            WHERE pf.code = @familyCode
              AND rf.target = 'REQUIREMENT'
              AND rf.status = 'VALIDATED'
            """;
        command.Parameters.AddWithValue("@familyCode", familyCode);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new RequirementFormulaRow(reader.GetString(0), reader.GetBoolean(1));
    }

    private static async Task<Dictionary<string, double>> BuildArgumentCoefficientMapAsync(
        SqlConnection connection,
        string familyCode,
        string? sizeCode,
        string? colorCode,
        CancellationToken cancellationToken)
    {
        var coefficients = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(sizeCode))
        {
            coefficients["SIZE"] = await LoadCoefficientAsync(connection, familyCode, "SIZE", sizeCode, cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(colorCode))
        {
            coefficients["COLOR"] = await LoadCoefficientAsync(connection, familyCode, "COLOR", colorCode, cancellationToken);
        }

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT ad.code
            FROM attribute_definitions ad
            WHERE ad.is_formula_argument = 1
              AND ad.is_active = 1
              AND ad.code NOT IN ('SIZE', 'COLOR')
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            coefficients.TryAdd(reader.GetString(0), 1.0);
        }

        return coefficients;
    }
}
