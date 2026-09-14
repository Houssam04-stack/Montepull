namespace Axioplan.GammesNomenclatures.E2E.Tests;

public static class AuditConfig
{
    public const string BaseUrl = "http://localhost:5280";
    public static readonly string SqlConnection = Environment.GetEnvironmentVariable("AXIOPLAN_CONNECTION_STRING_DOTNET") ??
        "Server=localhost\\SQLEXPRESS;Database=AxioplanMvp;Trusted_Connection=True;TrustServerCertificate=True;Encrypt=True;";
}

