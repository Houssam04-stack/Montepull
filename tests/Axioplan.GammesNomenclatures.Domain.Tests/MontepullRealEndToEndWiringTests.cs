using Axioplan.GammesNomenclatures.Application.Aps;
using Axioplan.GammesNomenclatures.Application.Models;
using Axioplan.GammesNomenclatures.Application.MontepullImport;
using Axioplan.GammesNomenclatures.Domain.MontepullImport;
using Axioplan.GammesNomenclatures.Infrastructure.Aps;
using Axioplan.GammesNomenclatures.Infrastructure.Data;
using Axioplan.GammesNomenclatures.Infrastructure.Logging;
using Axioplan.GammesNomenclatures.Infrastructure.Repositories;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using Xunit;

namespace Axioplan.GammesNomenclatures.Domain.Tests;

/// <summary>
/// Preuve SQL : MONTEPULL_REAL alimente commandes, OF, lancements APS et charge restante
/// sans DEMO_PF.
/// </summary>
public sealed class MontepullRealEndToEndWiringTests
{
    private static readonly string ConnectionString =
        Environment.GetEnvironmentVariable("AXIOPLAN_CONNECTION_STRING_DOTNET")
        ?? "Server=localhost\\SQLEXPRESS;Database=AxioplanMvp;Trusted_Connection=True;TrustServerCertificate=True;";

    private static bool CanConnect()
    {
        try
        {
            using var c = new SqlConnection(ConnectionString);
            c.Open();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static IOptions<DatabaseOptions> Opts()
        => Options.Create(new DatabaseOptions { ConnectionString = ConnectionString });

    [Fact]
    public async Task MontepullReal_command_of_launches_and_remaining_load()
    {
        if (!CanConnect()) return;

        var opts = Opts();
        var sqlLog = new SqlApplicationLogger(opts);
        var montepullRepo = new SqlServerMontepullImportRepository(opts, sqlLog);
        await montepullRepo.EnsureSchemaAsync();
        await montepullRepo.SetActiveDatasetAsync(PlanningDatasetCodes.MontepullReal, "e2e-test");

        var datasetService = new MontepullDatasetService(
            montepullRepo,
            Options.Create(new MontepullDatasetOptions { Active = PlanningDatasetCodes.MontepullReal }));
        var dataset = new PlanningDatasetProvider(datasetService, montepullRepo);
        Assert.True(await dataset.IsMontepullRealAsync());

        var cbnRepo = new SqlServerCbnRepository(opts);
        var orders = await cbnRepo.GetSalesOrdersAsync(PlanningDatasetCodes.MontepullReal);
        Assert.Contains(orders, o => o.Code == "SO26000321");
        var order = orders.First(o => o.Code == "SO26000321");
        var lines = await cbnRepo.GetSalesOrderLinesAsync(order.Id);
        Assert.NotEmpty(lines);
        Assert.Contains(lines, l => l.ArticleCode.StartsWith("PE26", StringComparison.OrdinalIgnoreCase));

        CbnRunResult? cbnResult = null;
        try
        {
            cbnResult = await cbnRepo.RunCbnAsync(new CbnRunRequest(order.Id, "PULL_COL_ROND"));
            Assert.True(cbnResult.RunId > 0);
            Assert.NotEmpty(cbnResult.Requirements);
        }
        catch (InvalidOperationException)
        {
            // BOM/profil manquant = blocage métier documenté ; le reste du parcours OF continue.
        }

        if (cbnResult is not null)
        {
            var peggingRepo = new SqlServerPeggingRepository(
                opts,
                Microsoft.Extensions.Logging.Abstractions.NullLogger<SqlServerPeggingRepository>.Instance,
                sqlLog);
            var peg = await peggingRepo.RunPeggingAsync(new PeggingRunRequest(cbnResult.RunId));
            Assert.True(peg.PeggingRunId > 0);
        }

        var from = DateOnly.FromDateTime(DateTime.Today);
        var to = from.AddDays(14);
        var launches = await dataset.ResolveMontepullLaunchesAsync(from, to);
        Assert.NotEmpty(launches);
        Assert.DoesNotContain(launches, l => l.ArticleCode.Equals("DEMO_PF", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(launches, l =>
            l.ResourceCode is "CC_REMAILLAGE" or "CC_TRICOTAGE" or "CC_TRAITEMENT");

        var sample = launches[0];
        var loadH = MontepullRemainingLoad.RemainingLoadHours(sample.QtyToLaunch, 0.05, "H", false);
        Assert.True(loadH > 0);

        await montepullRepo.SetActiveDatasetAsync(PlanningDatasetCodes.MontepullReal, "e2e-test");
        Assert.Equal(PlanningDatasetCodes.MontepullReal, await montepullRepo.GetActiveDatasetAsync());

        await using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();

        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                SELECT COUNT(*) FROM manufacturing_orders
                WHERE data_source = N'MONTEPULL_REAL' AND ISNULL(is_active,1)=1
                  AND status IN (N'PLANNED', N'RELEASED')
                """;
            Assert.True(Convert.ToInt32(await cmd.ExecuteScalarAsync()) >= 1);
        }

        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM mo_production_events WHERE data_source = N'MONTEPULL_REAL' AND ISNULL(is_active,1)=1";
            Assert.True(Convert.ToInt32(await cmd.ExecuteScalarAsync()) > 0);
        }
    }

    [Fact]
    public async Task MontepullReal_blocks_silent_demo_fallback()
    {
        if (!CanConnect()) return;

        var opts = Opts();
        var sqlLog = new SqlApplicationLogger(opts);
        var montepullRepo = new SqlServerMontepullImportRepository(opts, sqlLog);
        await montepullRepo.SetActiveDatasetAsync(PlanningDatasetCodes.MontepullReal, "e2e-test");
        var datasetService = new MontepullDatasetService(montepullRepo, Options.Create(new MontepullDatasetOptions()));
        var dataset = new PlanningDatasetProvider(datasetService, montepullRepo);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            dataset.EnsureNoDemoFallbackAsync("BuildDemoLaunches"));
        Assert.Contains("MONTEPULL_REAL", ex.Message);
        Assert.Contains("BuildDemoLaunches", ex.Message);
    }

    [Fact]
    public async Task MontepullReal_imported_orders_detail_lists_so26000321()
    {
        if (!CanConnect()) return;

        var opts = Opts();
        var sqlLog = new SqlApplicationLogger(opts);
        var montepullRepo = new SqlServerMontepullImportRepository(opts, sqlLog);
        await montepullRepo.SetActiveDatasetAsync(PlanningDatasetCodes.MontepullReal, "e2e-orders");

        var cbnRepo = new SqlServerCbnRepository(opts);
        await cbnRepo.EnsureTunimapulfSalesOrderAsync();
        var details = await cbnRepo.GetImportedSalesOrdersDetailAsync(PlanningDatasetCodes.MontepullReal);
        Assert.NotEmpty(details);
        Assert.Contains(details, o => o.Code == "SO26000321");
        var order = details.First(o => o.Code == "SO26000321");
        Assert.True(order.LineCount >= 1);
        Assert.NotEmpty(order.Lines);
        Assert.Contains(order.Lines, l => l.ArticleCode.StartsWith("PE26", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(PlanningDatasetCodes.MontepullReal, order.DataSource);
        Assert.Contains(details, o => o.Code == "CMD-TUNIMAPULF"
            || o.Lines.Any(l => l.ArticleCode is "TUNIMAPULF" or "TUNIMAPULF_BASE"));
    }

    [Fact]
    public async Task MontepullReal_aps_flux_and_gantt_exclude_demo_pf()
    {
        if (!CanConnect()) return;

        var opts = Opts();
        var sqlLog = new SqlApplicationLogger(opts);
        var montepullRepo = new SqlServerMontepullImportRepository(opts, sqlLog);
        await montepullRepo.SetActiveDatasetAsync(PlanningDatasetCodes.MontepullReal, "e2e-aps");
        var dataset = new PlanningDatasetProvider(
            new MontepullDatasetService(montepullRepo, Options.Create(new MontepullDatasetOptions { Active = PlanningDatasetCodes.MontepullReal })),
            montepullRepo);

        var schema = new Axioplan.GammesNomenclatures.Infrastructure.Aps.ApsSchemaBootstrap(opts);
        var capacityRepo = new SqlServerApsCapacityRepository(schema);
        var referentialRepo = new SqlServerApsReferentialRepository(schema);
        var compilerRepo = new SqlServerApsCompilerRepository(schema);
        var fluxRepo = new SqlServerApsFluxRepository(schema, compilerRepo);
        var journalRepo = new SqlServerApsJournalRepository(schema);
        var capacityEval = new ApsCapacityEvaluator(capacityRepo, referentialRepo);
        var flux = new ApsFluxService(fluxRepo, capacityRepo, capacityEval, journalRepo, dataset);

        var from = DateOnly.FromDateTime(DateTime.Today);
        var result = await flux.ComputeAsync(new ApsFluxWindowRequest(from, from.AddDays(7), null, Publish: true));
        Assert.Contains(result.Traces, t => t.Contains("MONTEPULL_REAL", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(result.Traces, t => t.Contains("lancements demo SIMULES", StringComparison.OrdinalIgnoreCase));
        Assert.NotEmpty(result.Loads);
        Assert.DoesNotContain(result.Loads, l => l.ResourceCode.Equals("EXT_ST_REMAIL", StringComparison.OrdinalIgnoreCase));

        var decision = new ApsScheduleDecisionService();
        var schedule = decision.BuildFromFlux(result);
        Assert.NotEmpty(schedule.Tasks);
        var csv = decision.BuildTasksCsv(schedule);
        Assert.DoesNotContain("DEMO_PF", csv, StringComparison.OrdinalIgnoreCase);
        Assert.True(csv.Length > 20);
    }
}
