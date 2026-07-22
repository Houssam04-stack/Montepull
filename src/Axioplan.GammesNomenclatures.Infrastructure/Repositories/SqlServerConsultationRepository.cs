using Axioplan.GammesNomenclatures.Application.Abstractions;
using Axioplan.GammesNomenclatures.Application.Models;
using Axioplan.GammesNomenclatures.Infrastructure.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace Axioplan.GammesNomenclatures.Infrastructure.Repositories;

public sealed class SqlServerConsultationRepository(IOptions<DatabaseOptions> options) : IConsultationRepository
{
    public async Task<IReadOnlyList<CbnRunListItem>> GetCbnRunsAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT cr.id, so.code, pf.code, cr.status, cr.created_at
            FROM cbn_runs cr
            JOIN sales_orders so ON so.id = cr.sales_order_id
            JOIN product_families pf ON pf.id = cr.product_family_id
            ORDER BY cr.created_at DESC
            """;

        var items = new List<CbnRunListItem>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new CbnRunListItem(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetDateTime(4)));
        }

        return items;
    }

    public async Task<IReadOnlyList<RoutingConsultationRow>> GetRoutingBaseAsync(
        string familyCode,
        CancellationToken cancellationToken = default)
    {
        await using var connection = OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT ro.operation_no, ro.name, wc.code, ro.quantity_base, ro.time_base, ro.time_unit, ro.behavior
            FROM routing_base_operations ro
            JOIN routing_bases rb ON rb.id = ro.routing_base_id
            JOIN product_families pf ON pf.id = rb.product_family_id
            JOIN workcenters wc ON wc.id = ro.workcenter_id
            WHERE pf.code = @familyCode
              AND rb.id = (
                  SELECT TOP 1 rb2.id
                  FROM routing_bases rb2
                  WHERE rb2.product_family_id = pf.id
                  ORDER BY rb2.version DESC, rb2.id DESC
              )
            ORDER BY ro.operation_no
            """;
        command.Parameters.AddWithValue("@familyCode", familyCode);

        var items = new List<RoutingConsultationRow>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new RoutingConsultationRow(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetDouble(3),
                reader.GetDouble(4),
                reader.GetString(5),
                reader.GetString(6)));
        }

        return items;
    }

    public async Task<IReadOnlyList<BomConsultationRow>> GetBomBaseAsync(
        string familyCode,
        CancellationToken cancellationToken = default)
    {
        await using var connection = OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT bl.line_no, a.code, a.label, bl.quantity_base, bl.unit, bl.loss_rate, bl.behavior
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

        var items = new List<BomConsultationRow>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new BomConsultationRow(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetDouble(3),
                reader.GetString(4),
                reader.GetDouble(5),
                reader.GetString(6)));
        }

        return items;
    }

    public async Task<IReadOnlyList<FlattenedBomConsultationRow>> GetFlattenedBomForRunAsync(
        int cbnRunId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT f.cbn_run_id, a.code, a.label, f.bom_level, f.quantity_per_unit, f.unit, f.source_path
            FROM cbn_flattened_bom_lines f
            JOIN articles a ON a.id = f.component_article_id
            WHERE f.cbn_run_id = @cbnRunId
            ORDER BY f.bom_level, f.bom_line_no
            """;
        command.Parameters.AddWithValue("@cbnRunId", cbnRunId);

        var items = new List<FlattenedBomConsultationRow>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new FlattenedBomConsultationRow(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetInt32(3),
                reader.GetDouble(4),
                reader.GetString(5),
                reader.GetString(6)));
        }

        return items;
    }

    private SqlConnection OpenConnection()
    {
        var connectionString = options.Value.ConnectionString;
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("Chaine de connexion SQL Server introuvable.");
        }

        var connection = new SqlConnection(connectionString);
        connection.Open();
        return connection;
    }
}
