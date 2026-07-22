using System.Text;
using System.Text.Json;
using Axioplan.GammesNomenclatures.Application.Abstractions;
using Axioplan.GammesNomenclatures.Application.Models;
using Axioplan.GammesNomenclatures.Domain.Aps.Compiler;
using Axioplan.GammesNomenclatures.Domain.Aps.Expectations;
using Axioplan.GammesNomenclatures.Domain.Aps.Journal;
using Axioplan.GammesNomenclatures.Domain.Aps.Referential;
using Axioplan.GammesNomenclatures.Infrastructure.Aps;
using Microsoft.Data.SqlClient;

namespace Axioplan.GammesNomenclatures.Infrastructure.Repositories;

public sealed class SqlServerApsJournalRepository(ApsSchemaBootstrap schema) : IApsJournalRepository
{
    public Task EnsureSchemaAsync(CancellationToken cancellationToken = default)
        => schema.EnsureAsync(cancellationToken);

    public async Task<ApsJournalEvent> AppendFactAsync(
        AppendApsJournalFactCommand command,
        CancellationToken cancellationToken = default)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var connection = schema.OpenConnection();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO aps_journal_events
                (event_type, occurred_at, recorded_at, aggregate_id, payload_json, actor, schema_version, causation_id, correlation_id)
            OUTPUT INSERTED.event_id, INSERTED.event_type, INSERTED.occurred_at, INSERTED.recorded_at,
                   INSERTED.aggregate_id, INSERTED.payload_json, INSERTED.actor, INSERTED.schema_version,
                   INSERTED.causation_id, INSERTED.correlation_id
            VALUES (@type, @occurred, SYSUTCDATETIME(), @agg, @payload, @actor, @ver, @cau, @cor)
            """;
        cmd.Parameters.AddWithValue("@type", command.EventType);
        cmd.Parameters.AddWithValue("@occurred", command.OccurredAtUtc);
        cmd.Parameters.AddWithValue("@agg", command.AggregateId);
        cmd.Parameters.AddWithValue("@payload", command.PayloadJson);
        cmd.Parameters.AddWithValue("@actor", command.Actor);
        cmd.Parameters.AddWithValue("@ver", command.SchemaVersion);
        cmd.Parameters.AddWithValue("@cau", (object?)command.CausationId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@cor", (object?)command.CorrelationId ?? DBNull.Value);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("Echec append journal.");
        }

        return ReadEvent(reader);
    }

    public async Task<IReadOnlyList<ApsJournalEvent>> QueryFactsAsync(
        ApsJournalQuery query,
        CancellationToken cancellationToken = default)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var connection = schema.OpenConnection();
        await using var cmd = connection.CreateCommand();
        var sql = new StringBuilder("""
            SELECT TOP (@take) event_id, event_type, occurred_at, recorded_at, aggregate_id,
                   payload_json, actor, schema_version, causation_id, correlation_id
            FROM aps_journal_events WHERE 1=1
            """);
        cmd.Parameters.AddWithValue("@take", Math.Clamp(query.Take, 1, 1000));
        if (!string.IsNullOrWhiteSpace(query.EventType))
        {
            sql.Append(" AND event_type = @type");
            cmd.Parameters.AddWithValue("@type", query.EventType);
        }

        if (!string.IsNullOrWhiteSpace(query.AggregateId))
        {
            sql.Append(" AND aggregate_id = @agg");
            cmd.Parameters.AddWithValue("@agg", query.AggregateId);
        }

        if (query.FromUtc is not null)
        {
            sql.Append(" AND occurred_at >= @from");
            cmd.Parameters.AddWithValue("@from", query.FromUtc.Value);
        }

        if (query.ToUtc is not null)
        {
            sql.Append(" AND occurred_at <= @to");
            cmd.Parameters.AddWithValue("@to", query.ToUtc.Value);
        }

        sql.Append(" ORDER BY event_id DESC");
        cmd.CommandText = sql.ToString();

        var list = new List<ApsJournalEvent>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(ReadEvent(reader));
        }

        return list;
    }

    private static ApsJournalEvent ReadEvent(SqlDataReader reader)
        => new(
            reader.GetInt64(0),
            reader.GetString(1),
            reader.GetDateTime(2),
            reader.GetDateTime(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetString(6),
            reader.GetString(7),
            reader.IsDBNull(8) ? null : reader.GetString(8),
            reader.IsDBNull(9) ? null : reader.GetString(9));
}

public sealed class SqlServerApsExpectationRepository(ApsSchemaBootstrap schema) : IApsExpectationRepository
{
    public Task EnsureSchemaAsync(CancellationToken cancellationToken = default)
        => schema.EnsureAsync(cancellationToken);

    public async Task<bool> ExistsAsync(string expectedId, CancellationToken cancellationToken = default)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var connection = schema.OpenConnection();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM aps_expectations WHERE expected_id = @id";
        cmd.Parameters.AddWithValue("@id", expectedId);
        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return result is not null && result is not DBNull;
    }

    public async Task<ApsExpectation> EmitAsync(
        EmitApsExpectationCommand command,
        CancellationToken cancellationToken = default)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var connection = schema.OpenConnection();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO aps_expectations
                (expected_id, expected_type, emitted_by, grain, plan_ref, earliest_at, latest_at,
                 hre, causation_id, aggregate_id, payload_json, emitted_at, schema_version)
            OUTPUT INSERTED.id, INSERTED.expected_id, INSERTED.expected_type, INSERTED.emitted_by, INSERTED.grain,
                   INSERTED.plan_ref, INSERTED.earliest_at, INSERTED.latest_at, INSERTED.hre, INSERTED.causation_id,
                   INSERTED.aggregate_id, INSERTED.payload_json, INSERTED.emitted_at, INSERTED.schema_version
            VALUES (@eid, @etype, @by, @grain, @plan, @early, @late, @hre, @cau, @agg, @payload, SYSUTCDATETIME(), @ver)
            """;
        cmd.Parameters.AddWithValue("@eid", command.ExpectedId);
        cmd.Parameters.AddWithValue("@etype", command.ExpectedType);
        cmd.Parameters.AddWithValue("@by", command.EmittedBy);
        cmd.Parameters.AddWithValue("@grain", command.Grain);
        cmd.Parameters.AddWithValue("@plan", (object?)command.PlanRef ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@early", command.EarliestAtUtc);
        cmd.Parameters.AddWithValue("@late", command.LatestAtUtc);
        cmd.Parameters.AddWithValue("@hre", (object?)command.Hre ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@cau", (object?)command.CausationId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@agg", command.AggregateId);
        cmd.Parameters.AddWithValue("@payload", command.PayloadJson);
        cmd.Parameters.AddWithValue("@ver", command.SchemaVersion);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("Echec emission attendu.");
        }

        return Read(reader);
    }

    public async Task<IReadOnlyList<ApsExpectation>> QueryAsync(
        ApsExpectationQuery query,
        CancellationToken cancellationToken = default)
    {
        await schema.EnsureAsync(cancellationToken);
        await using var connection = schema.OpenConnection();
        await using var cmd = connection.CreateCommand();
        var sql = new StringBuilder("""
            SELECT TOP (@take) id, expected_id, expected_type, emitted_by, grain, plan_ref,
                   earliest_at, latest_at, hre, causation_id, aggregate_id, payload_json, emitted_at, schema_version
            FROM aps_expectations WHERE 1=1
            """);
        cmd.Parameters.AddWithValue("@take", Math.Clamp(query.Take, 1, 1000));
        if (!string.IsNullOrWhiteSpace(query.ExpectedType))
        {
            sql.Append(" AND expected_type = @t");
            cmd.Parameters.AddWithValue("@t", query.ExpectedType);
        }

        if (!string.IsNullOrWhiteSpace(query.AggregateId))
        {
            sql.Append(" AND aggregate_id = @a");
            cmd.Parameters.AddWithValue("@a", query.AggregateId);
        }

        if (!string.IsNullOrWhiteSpace(query.Grain))
        {
            sql.Append(" AND grain = @g");
            cmd.Parameters.AddWithValue("@g", query.Grain);
        }

        if (!string.IsNullOrWhiteSpace(query.EmittedBy))
        {
            sql.Append(" AND emitted_by = @e");
            cmd.Parameters.AddWithValue("@e", query.EmittedBy);
        }

        sql.Append(" ORDER BY id DESC");
        cmd.CommandText = sql.ToString();

        var list = new List<ApsExpectation>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(Read(reader));
        }

        return list;
    }

    private static ApsExpectation Read(SqlDataReader reader)
        => new(
            reader.GetInt64(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetString(5),
            reader.GetDateTime(6),
            reader.GetDateTime(7),
            reader.IsDBNull(8) ? null : reader.GetDouble(8),
            reader.IsDBNull(9) ? null : reader.GetString(9),
            reader.GetString(10),
            reader.GetString(11),
            reader.GetDateTime(12),
            reader.GetString(13));
}
