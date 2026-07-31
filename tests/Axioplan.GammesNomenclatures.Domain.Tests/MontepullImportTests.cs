using System.Globalization;
using Axioplan.GammesNomenclatures.Domain.MontepullImport;
using Axioplan.GammesNomenclatures.Infrastructure.Data;
using Axioplan.GammesNomenclatures.Infrastructure.Logging;
using Axioplan.GammesNomenclatures.Infrastructure.Repositories;
using Microsoft.Extensions.Options;
using Xunit;

namespace Axioplan.GammesNomenclatures.Domain.Tests;

public sealed class MontepullImportDomainTests
{
    private static string FixturesDir =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "fixtures", "montepull"));

    private static byte[] ReadFixture(string fileName)
    {
        var path = Path.Combine(FixturesDir, fileName);
        Assert.True(File.Exists(path), $"Fixture manquante : {path}");
        return File.ReadAllBytes(path);
    }

    [Theory]
    [InlineData("commandes_sample.xlsx", MontepullFileKind.Commandes)]
    [InlineData("ListeSuivi_sample.xlsx", MontepullFileKind.ListeSuivi)]
    [InlineData("SuiviOperations_sample.xlsx", MontepullFileKind.SuiviOperations)]
    [InlineData("DOUBLYGILF_sample.xlsx", MontepullFileKind.Doublygilf)]
    [InlineData("Nomenclature_DOULBYGILF_sample.xlsx", MontepullFileKind.NomenclatureDoulbygilf)]
    public void FileDetector_detects_kind_from_fixture_name(string fileName, MontepullFileKind expected)
    {
        var kind = MontepullFileDetector.Detect(fileName, Array.Empty<string>());
        Assert.Equal(expected, kind);
    }

    [Fact]
    public void QuantityRules_remaining_and_launch_status()
    {
        Assert.Equal(100, MontepullQuantityRules.RemainingToLaunch(100, 0));
        Assert.Equal(50, MontepullQuantityRules.RemainingToLaunch(100, 50));
        Assert.Equal(0, MontepullQuantityRules.RemainingToLaunch(100, 120));
        Assert.Equal(60, MontepullQuantityRules.RemainingToProduce(100, 40));
        Assert.Equal("NOT_LAUNCHED", MontepullQuantityRules.LaunchStatus(100, 0, 0));
        Assert.Equal("PARTIALLY_LAUNCHED", MontepullQuantityRules.LaunchStatus(100, 40, 10));
        Assert.Equal("FULLY_LAUNCHED", MontepullQuantityRules.LaunchStatus(100, 100, 50));
        Assert.Equal("COMPLETED", MontepullQuantityRules.LaunchStatus(100, 100, 100));
    }

    [Fact]
    public void DurationAnalyzer_classifies_scan_process_and_invalid()
    {
        var start = new DateTime(2026, 7, 1, 10, 0, 0);
        var scan = MontepullDurationAnalyzer.Analyze(start, start.AddSeconds(2));
        Assert.Equal("SCAN", scan.Kind);
        Assert.Equal(2, scan.Seconds);

        var process = MontepullDurationAnalyzer.Analyze(start, start.AddHours(1));
        Assert.Equal("PROCESS", process.Kind);

        var invalid = MontepullDurationAnalyzer.Analyze(start, start.AddHours(-1));
        Assert.Equal("INVALID", invalid.Kind);
        Assert.Equal("DATE_END_BEFORE_START", invalid.AnomalyCode);
    }

    [Fact]
    public void DurationStats_are_robust()
    {
        var values = new[] { 10.0, 20, 30, 40, 100 };
        var stats = MontepullDurationAnalyzer.ComputeStats(values, excludedAnomalies: 2);
        Assert.Equal(5, stats.ObservationCount);
        Assert.Equal(40, stats.MeanSeconds);
        Assert.Equal(30, stats.MedianSeconds);
        Assert.Equal(10, stats.MinSeconds);
        Assert.Equal(100, stats.MaxSeconds);
        Assert.Equal(2, stats.ExcludedAnomalyCount);
        Assert.NotNull(stats.P25Seconds);
        Assert.NotNull(stats.P75Seconds);
        Assert.NotNull(stats.StdDevSeconds);
    }

    [Fact]
    public void Aggregation_by_of_operation_keeps_packets()
    {
        var rows = new[]
        {
            ("OF1", 30, 20.0, 0.0, (DateTime?)new DateTime(2026, 7, 1, 10, 0, 0), (DateTime?)new DateTime(2026, 7, 1, 10, 0, 2), "1", "Terminé", (double?)50),
            ("OF1", 30, 15.0, 1.0, (DateTime?)new DateTime(2026, 7, 1, 11, 0, 0), (DateTime?)new DateTime(2026, 7, 1, 11, 0, 1), "2", "Terminé", (double?)50),
        };
        var agg = MontepullAggregation.AggregateByOfOperation(rows);
        Assert.Single(agg);
        Assert.Equal(35, agg[0].QtyGoodTotal);
        Assert.Equal(1, agg[0].QtyRejectedTotal);
        Assert.Equal(2, agg[0].PacketCount);
        Assert.Equal(15, agg[0].RemainingQty);
    }

    [Fact]
    public void RemainingLoad_zero_when_completed()
    {
        Assert.Equal(0, MontepullRemainingLoad.RemainingLoadHours(10, 0.5, "H", operationCompleted: true));
        Assert.Equal(5, MontepullRemainingLoad.RemainingLoadHours(10, 0.5, "H", operationCompleted: false));
        Assert.Equal(1, MontepullRemainingLoad.RemainingLoadHours(60, 1, "MIN", operationCompleted: false));
    }

    [Fact]
    public void ParseCommande_maps_columns_and_ignores_somme_as_produced()
    {
        var headers = new[]
        {
            "Commande", "Client", "Article", "Designation", "Qte_Commandee", "Qte_Lancee",
            "Somme_Operations", "Somme_Op_70_Commande", "Somme_Op_70_Article", "Derniere_Operation",
            "Qte_Fabriquee_Derniere_Operation"
        };
        var map = MontepullColumnIndex.MapHeaders(headers);
        var row = new[] { "SO1", "CL1", "A1", "Art", "100", "40", "999", "0", "0", "30", "25" };
        var parsed = MontepullParsers.ParseCommande(row, map, 2);
        Assert.NotNull(parsed);
        Assert.Equal(100, parsed!.QtyOrdered);
        Assert.Equal(40, parsed.QtyLaunched);
        Assert.Equal(25, parsed.QtyProduced);
        Assert.Equal(999, parsed.SommeOperations);
        Assert.Equal(60, parsed.RemainingToLaunch);
        Assert.Equal(75, parsed.RemainingToProduce);
        Assert.Equal("PARTIALLY_LAUNCHED", parsed.LaunchStatus);
    }

    [Fact]
    public void ParseSuivi_event_hash_differs_for_packets()
    {
        var headers = new[]
        {
            "N° OF", "N° CMD", "Article", "Désignation", "Opération", "Date Début", "Date Fin",
            "Quantité Réelle", "Quantité Rejet", "Qte OF", "Npaquet", "Statut"
        };
        var map = MontepullColumnIndex.MapHeaders(headers);
        var row1 = new[] { "OF1", "SO1", "A1", "d", "30", "2026-07-01 10:00:00", "2026-07-01 10:00:02", "20", "0", "50", "1", "Terminé" };
        var row2 = new[] { "OF1", "SO1", "A1", "d", "30", "2026-07-01 11:00:00", "2026-07-01 11:00:01", "15", "1", "50", "2", "Terminé" };
        var a = MontepullParsers.ParseSuivi(row1, map, 2, "Details", "LISTE");
        var b = MontepullParsers.ParseSuivi(row2, map, 3, "Details", "LISTE");
        Assert.NotNull(a);
        Assert.NotNull(b);
        Assert.NotEqual(a!.RowHash, b!.RowHash);
        Assert.Equal("SCAN", a.DurationKind);
    }

    [Fact]
    public void ColumnIndex_parses_dates_and_numeric_fr()
    {
        var headers = new[] { "Date Debut", "Qte" };
        var map = MontepullColumnIndex.MapHeaders(headers);
        var row = new[] { "2026-07-01 10:24:39", "12,5" };
        var dt = MontepullColumnIndex.GetDateTime(row, map, "Date Debut");
        Assert.NotNull(dt);
        Assert.Equal(10, dt!.Value.Hour);
        Assert.True(MontepullColumnIndex.TryGetDouble(row, map, out var qty, "Qte"));
        Assert.Equal(12.5, qty);
    }

    [Fact]
    public void Empty_and_blank_rows_are_ignored_by_parsers()
    {
        var map = MontepullColumnIndex.MapHeaders(new[] { "Commande", "Article", "Qte_Commandee" });
        Assert.Null(MontepullParsers.ParseCommande(new[] { "", "", "" }, map, 1));
        var suiviMap = MontepullColumnIndex.MapHeaders(new[] { "N° OF", "Opération" });
        Assert.Null(MontepullParsers.ParseSuivi(new[] { "", "" }, suiviMap, 1, "x", "LISTE"));
    }
}

public sealed class MontepullImportSqlIntegrationTests
{
    private static readonly string? ConnectionString =
        Environment.GetEnvironmentVariable("AXIOPLAN_CONNECTION_STRING_DOTNET")
        ?? "Server=localhost\\SQLEXPRESS;Database=AxioplanMvp;Trusted_Connection=True;TrustServerCertificate=True;";

    private static string FixturesDir =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "fixtures", "montepull"));

    private static bool CanConnect()
    {
        try
        {
            using var connection = new Microsoft.Data.SqlClient.SqlConnection(ConnectionString);
            connection.Open();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static SqlServerMontepullImportRepository CreateRepo()
    {
        var opts = Options.Create(new DatabaseOptions { ConnectionString = ConnectionString! });
        return new SqlServerMontepullImportRepository(opts, new SqlApplicationLogger(opts));
    }

    private static byte[] ReadFixture(string fileName) => File.ReadAllBytes(Path.Combine(FixturesDir, fileName));

    [Fact]
    public async Task Analyze_each_fixture_detects_sheets_and_kind()
    {
        if (!CanConnect()) return;
        var repo = CreateRepo();
        await repo.EnsureSchemaAsync();

        var commandes = await repo.AnalyzeFileAsync(ReadFixture("commandes_sample.xlsx"), "commandes_sample.xlsx");
        Assert.Equal(MontepullFileKind.Commandes, commandes.Kind);
        Assert.Contains(commandes.SheetNames, s => s.Contains("Commande", StringComparison.OrdinalIgnoreCase));
        Assert.True(commandes.EstimatedRows >= 3);

        var liste = await repo.AnalyzeFileAsync(ReadFixture("ListeSuivi_sample.xlsx"), "ListeSuivi_sample.xlsx");
        Assert.Equal(MontepullFileKind.ListeSuivi, liste.Kind);
        Assert.True(liste.SheetNames.Count >= 3);

        var suivi = await repo.AnalyzeFileAsync(ReadFixture("SuiviOperations_sample.xlsx"), "SuiviOperations_sample.xlsx");
        Assert.Equal(MontepullFileKind.SuiviOperations, suivi.Kind);

        var doub = await repo.AnalyzeFileAsync(ReadFixture("DOUBLYGILF_sample.xlsx"), "DOUBLYGILF_sample.xlsx");
        Assert.Equal(MontepullFileKind.Doublygilf, doub.Kind);

        var nom = await repo.AnalyzeFileAsync(ReadFixture("Nomenclature_DOULBYGILF_sample.xlsx"), "Nomenclature_DOULBYGILF_sample.xlsx");
        Assert.Equal(MontepullFileKind.NomenclatureDoulbygilf, nom.Kind);
    }

    [Fact]
    public async Task Stage_validate_promote_idempotent_and_cancel()
    {
        if (!CanConnect()) return;
        var repo = CreateRepo();
        await repo.EnsureSchemaAsync();

        var files = new List<(byte[] Content, string FileName)>
        {
            (ReadFixture("commandes_sample.xlsx"), "commandes_sample.xlsx"),
            (ReadFixture("ListeSuivi_sample.xlsx"), "ListeSuivi_sample.xlsx"),
            (ReadFixture("SuiviOperations_sample.xlsx"), "SuiviOperations_sample.xlsx"),
            (ReadFixture("DOUBLYGILF_sample.xlsx"), "DOUBLYGILF_sample.xlsx"),
            (ReadFixture("Nomenclature_DOULBYGILF_sample.xlsx"), "Nomenclature_DOULBYGILF_sample.xlsx"),
        };

        var staged = await repo.StageFilesAsync(files, "tests", "UNIT_TEST");
        Assert.True(staged.BatchId > 0);
        Assert.True(staged.LinesRead > 0);
        Assert.True(staged.CommandesDetected >= 3);
        Assert.True(staged.OfDetected >= 1);
        Assert.True(staged.ArticlesDetected >= 1);
        Assert.Contains(staged.Anomalies, a => a.Code is "UNKNOWN_OPERATION" or "DATE_END_BEFORE_START" or "NEGATIVE_QTY");

        var validated = await repo.ValidateBatchAsync(staged.BatchId);
        Assert.True(validated.Status is "VALIDATED" or "STAGING" or "FAILED" or "PROMOTED");

        var promoted = await repo.PromoteBatchAsync(staged.BatchId);
        Assert.Equal("PROMOTED", promoted.Status);

        var orders = await repo.ListImportedManufacturingOrdersAsync();
        Assert.Contains(orders, o => o.OfCode.StartsWith("OF_TEST_", StringComparison.Ordinal));

        // Réimport idempotent : pas de doublon d'événements (hash global)
        var staged2 = await repo.StageFilesAsync(files, "tests", "UNIT_TEST_REIMPORT");
        var promoted2 = await repo.PromoteBatchAsync(staged2.BatchId);
        Assert.Equal("PROMOTED", promoted2.Status);

        await repo.CancelBatchAsync(staged2.BatchId);
        var csv = await repo.GetAnomaliesReportCsvAsync(staged.BatchId);
        Assert.Contains("Severite", csv, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Code", csv, StringComparison.OrdinalIgnoreCase);

        await repo.SetActiveDatasetAsync("MONTEPULL_REAL", "tests");
        Assert.Equal("MONTEPULL_REAL", await repo.GetActiveDatasetAsync());
        await repo.SetActiveDatasetAsync("DEMO", "tests");
    }

    [Fact]
    public void Remaining_load_and_gantt_inputs_from_domain()
    {
        // Charge restante alimente APS via MontepullRemainingLoad (temps validé, pas moyenne brute).
        var hours = MontepullRemainingLoad.RemainingLoadHours(remainingQty: 15, validatedUnitTime: 2, timeUnit: "MIN", operationCompleted: false);
        Assert.Equal(0.5, hours, 5);

        var completed = MontepullRemainingLoad.RemainingLoadHours(15, 2, "MIN", operationCompleted: true);
        Assert.Equal(0, completed);
    }
}
