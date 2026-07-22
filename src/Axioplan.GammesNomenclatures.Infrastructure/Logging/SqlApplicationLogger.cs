using Axioplan.GammesNomenclatures.Infrastructure.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace Axioplan.GammesNomenclatures.Infrastructure.Logging;

public sealed class SqlApplicationLogger(IOptions<DatabaseOptions> options)
{
    public async Task LogAsync(
        string level,
        string category,
        string message,
        string? details = null,
        CancellationToken cancellationToken = default)
    {
        var connectionString = options.Value.ConnectionString;
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO application_logs (level, category, message, details)
            VALUES (@level, @category, @message, @details)
            """;
        command.Parameters.AddWithValue("@level", level);
        command.Parameters.AddWithValue("@category", category);
        command.Parameters.AddWithValue("@message", message);
        command.Parameters.AddWithValue("@details", (object?)details ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
