using Microsoft.Data.SqlClient;

namespace Axioplan.GammesNomenclatures.E2E.Tests;

public static class SqlAuditHelper
{
    public static async Task<int> ScalarIntAsync(string sql)
    {
        await using var c = new SqlConnection(AuditConfig.SqlConnection);
        await c.OpenAsync();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        var o = await cmd.ExecuteScalarAsync();
        return o is null or DBNull ? 0 : Convert.ToInt32(o);
    }

    public static async Task<string?> ScalarStringAsync(string sql)
    {
        await using var c = new SqlConnection(AuditConfig.SqlConnection);
        await c.OpenAsync();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        var o = await cmd.ExecuteScalarAsync();
        return o is null or DBNull ? null : o.ToString();
    }
}
