using Axioplan.GammesNomenclatures.Application.Abstractions;
using Axioplan.GammesNomenclatures.Application.Models;
using Axioplan.GammesNomenclatures.Infrastructure.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace Axioplan.GammesNomenclatures.Infrastructure.Repositories;

public sealed class SqlServerStockRepository(IOptions<DatabaseOptions> options) : IStockRepository
{
    public async Task EnsureSchemaAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            IF OBJECT_ID(N'dbo.stock_lot_lines', N'U') IS NULL
            BEGIN
                CREATE TABLE stock_lot_lines (
                    id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    article_id INT NOT NULL,
                    location_code NVARCHAR(50) NULL,
                    warehouse NVARCHAR(50) NULL,
                    lot_code NVARCHAR(80) NULL,
                    supplier_lot NVARCHAR(80) NULL,
                    quantity_allocated FLOAT NOT NULL DEFAULT 0,
                    quantity_in_progress FLOAT NOT NULL DEFAULT 0,
                    quantity_in_stock FLOAT NOT NULL DEFAULT 0,
                    quantity_available FLOAT NOT NULL DEFAULT 0,
                    status NVARCHAR(20) NULL,
                    location_type NVARCHAR(30) NULL,
                    unit NVARCHAR(20) NOT NULL,
                    received_date DATE NULL,
                    last_receipt_date DATE NULL,
                    last_issue_date DATE NULL,
                    updated_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
                    FOREIGN KEY (article_id) REFERENCES articles(id)
                );
                CREATE INDEX IX_stock_lot_lines_article ON stock_lot_lines(article_id);
                CREATE INDEX IX_stock_lot_lines_lot ON stock_lot_lines(lot_code);
            END;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<StockBalanceRow>> GetStockBalancesAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT sb.id,
                   sb.article_id,
                   a.code,
                   a.label,
                   a.article_type,
                   ac.label,
                   af.label,
                   sb.quantity_available,
                   sb.unit,
                   sb.updated_at
            FROM stock_balances sb
            JOIN articles a ON a.id = sb.article_id
            JOIN article_families af ON af.id = a.family_id
            JOIN article_categories ac ON ac.id = af.category_id
            ORDER BY a.code
            """;

        var items = new List<StockBalanceRow>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new StockBalanceRow(
                reader.GetInt32(0),
                reader.GetInt32(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.GetDouble(7),
                reader.GetString(8),
                reader.GetDateTime(9)));
        }

        return items;
    }

    public async Task<IReadOnlyList<StockLotLineRow>> GetStockLotLinesAsync(CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);
        await using var connection = OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT sl.id,
                   sl.article_id,
                   a.code,
                   a.label,
                   a.article_type,
                   ac.label,
                   af.label,
                   sl.location_code,
                   sl.warehouse,
                   sl.lot_code,
                   sl.supplier_lot,
                   sl.quantity_allocated,
                   sl.quantity_in_progress,
                   sl.quantity_in_stock,
                   sl.quantity_available,
                   sl.status,
                   sl.location_type,
                   sl.unit,
                   sl.received_date,
                   sl.last_receipt_date,
                   sl.last_issue_date,
                   sl.updated_at
            FROM stock_lot_lines sl
            JOIN articles a ON a.id = sl.article_id
            JOIN article_families af ON af.id = a.family_id
            JOIN article_categories ac ON ac.id = af.category_id
            ORDER BY a.code, sl.lot_code, sl.location_code
            """;

        var items = new List<StockLotLineRow>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new StockLotLineRow(
                reader.GetInt32(0),
                reader.GetInt32(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetString(8),
                reader.IsDBNull(9) ? null : reader.GetString(9),
                reader.IsDBNull(10) ? null : reader.GetString(10),
                reader.GetDouble(11),
                reader.GetDouble(12),
                reader.GetDouble(13),
                reader.GetDouble(14),
                reader.IsDBNull(15) ? null : reader.GetString(15),
                reader.IsDBNull(16) ? null : reader.GetString(16),
                reader.GetString(17),
                reader.IsDBNull(18) ? null : DateOnly.FromDateTime(reader.GetDateTime(18)),
                reader.IsDBNull(19) ? null : DateOnly.FromDateTime(reader.GetDateTime(19)),
                reader.IsDBNull(20) ? null : DateOnly.FromDateTime(reader.GetDateTime(20)),
                reader.GetDateTime(21)));
        }

        return items;
    }

    public async Task<IReadOnlyList<StockNomenclatureLineRow>> GetNomenclatureLinesAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT bl.id,
                   bb.code,
                   bb.version,
                   pf.code,
                   parent_a.code,
                   parent_a.label,
                   bl.line_no,
                   comp_a.id,
                   comp_a.code,
                   comp_a.label,
                   comp_a.article_type,
                   ac.label,
                   af.label,
                   bl.quantity_base,
                   bl.unit,
                   bl.loss_rate,
                   bl.behavior
            FROM bom_base_lines bl
            INNER JOIN bom_bases bb ON bb.id = bl.bom_base_id
            LEFT JOIN product_families pf ON pf.id = bb.product_family_id
            INNER JOIN articles comp_a ON comp_a.id = bl.component_article_id
            INNER JOIN article_families af ON af.id = comp_a.family_id
            INNER JOIN article_categories ac ON ac.id = af.category_id
            OUTER APPLY (
                SELECT TOP 1 a.code, a.label
                FROM article_bom_assignments aba
                INNER JOIN articles a ON a.id = aba.article_id
                WHERE aba.bom_base_id = bb.id
                ORDER BY a.code
            ) parent_a
            ORDER BY bb.code, bl.line_no, comp_a.code
            """;

        var items = new List<StockNomenclatureLineRow>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new StockNomenclatureLineRow(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetInt32(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.GetInt32(6),
                reader.GetInt32(7),
                reader.GetString(8),
                reader.GetString(9),
                reader.GetString(10),
                reader.GetString(11),
                reader.GetString(12),
                reader.GetDouble(13),
                reader.GetString(14),
                reader.GetDouble(15),
                reader.GetString(16)));
        }

        return items;
    }

    public async Task<StockFilterOptionsDto> GetFilterOptionsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = OpenConnection();

        async Task<List<StockFilterOption>> LoadCategoriesAsync()
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                SELECT DISTINCT ac.code, ac.label
                FROM stock_balances sb
                JOIN articles a ON a.id = sb.article_id
                JOIN article_families af ON af.id = a.family_id
                JOIN article_categories ac ON ac.id = af.category_id
                ORDER BY ac.label
                """;
            var list = new List<StockFilterOption>();
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                list.Add(new StockFilterOption(reader.GetString(0), reader.GetString(1)));
            }

            return list;
        }

        async Task<List<StockFilterOption>> LoadFamiliesAsync()
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                SELECT DISTINCT af.code, af.label
                FROM stock_balances sb
                JOIN articles a ON a.id = sb.article_id
                JOIN article_families af ON af.id = a.family_id
                ORDER BY af.label
                """;
            var list = new List<StockFilterOption>();
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                list.Add(new StockFilterOption(reader.GetString(0), reader.GetString(1)));
            }

            return list;
        }

        async Task<List<string>> LoadArticleTypesAsync()
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                SELECT DISTINCT a.article_type
                FROM stock_balances sb
                JOIN articles a ON a.id = sb.article_id
                ORDER BY a.article_type
                """;
            var list = new List<string>();
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                list.Add(reader.GetString(0));
            }

            return list;
        }

        async Task<List<StockFilterOption>> LoadBomBasesAsync()
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                SELECT DISTINCT bb.code, bb.code + N' (v' + CAST(bb.version AS NVARCHAR(10)) + N')'
                FROM bom_bases bb
                ORDER BY bb.code
                """;
            var list = new List<StockFilterOption>();
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                list.Add(new StockFilterOption(reader.GetString(0), reader.GetString(1)));
            }

            return list;
        }

        var categories = await LoadCategoriesAsync();
        var families = await LoadFamiliesAsync();
        var articleTypes = await LoadArticleTypesAsync();
        var bomBases = await LoadBomBasesAsync();
        return new StockFilterOptionsDto(categories, families, articleTypes, bomBases);
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
}
