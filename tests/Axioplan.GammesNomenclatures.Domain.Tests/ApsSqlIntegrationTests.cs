using Axioplan.GammesNomenclatures.Application.Aps;
using Axioplan.GammesNomenclatures.Application.Models;
using Axioplan.GammesNomenclatures.Domain.Aps.Compiler;
using Axioplan.GammesNomenclatures.Domain.Aps.Expectations;
using Axioplan.GammesNomenclatures.Domain.Aps.Journal;
using Axioplan.GammesNomenclatures.Infrastructure.Aps;
using Axioplan.GammesNomenclatures.Infrastructure.Data;
using Axioplan.GammesNomenclatures.Infrastructure.Repositories;
using Microsoft.Extensions.Options;
using Xunit;

namespace Axioplan.GammesNomenclatures.Domain.Tests;

/// <summary>
/// Tests d'integration SQL Server — skips si la base n'est pas joignable.
/// </summary>
public sealed class ApsSqlIntegrationTests
{
    private static readonly string? ConnectionString =
        Environment.GetEnvironmentVariable("AXIOPLAN_CONNECTION_STRING_DOTNET")
        ?? "Server=localhost\\SQLEXPRESS;Database=AxioplanMvp;Trusted_Connection=True;TrustServerCertificate=True;";

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

    private static ApsSchemaBootstrap Bootstrap()
        => new(Options.Create(new DatabaseOptions { ConnectionString = ConnectionString }));

    [Fact]
    public async Task Journal_append_only_preserves_occurred_vs_recorded()
    {
        if (!CanConnect())
        {
            return;
        }

        var bootstrap = Bootstrap();
        var repo = new SqlServerApsJournalRepository(bootstrap);
        var service = new ApsJournalService(repo);

        var occurred = DateTime.UtcNow.AddHours(-5);
        var created = await service.AppendFactAsync(new AppendApsJournalFactCommand(
            ApsFactEventTypes.OperationFinished,
            occurred,
            "order:test-" + Guid.NewGuid().ToString("N")[..8],
            """{"qty":1}""",
            "test"));

        Assert.True(created.EventId > 0);
        Assert.Equal(occurred.ToString("yyyy-MM-dd HH:mm"), created.OccurredAtUtc.ToString("yyyy-MM-dd HH:mm"));
        Assert.True(created.RecordedAtUtc > created.OccurredAtUtc);
    }

    [Fact]
    public async Task Expectation_is_immutable_and_replan_creates_new_id()
    {
        if (!CanConnect())
        {
            return;
        }

        var bootstrap = Bootstrap();
        var repo = new SqlServerApsExpectationRepository(bootstrap);
        var service = new ApsExpectationService(repo);

        var id1 = "exp-" + Guid.NewGuid().ToString("N");
        var cmd = new EmitApsExpectationCommand(
            id1,
            ApsExpectationTypes.OpenContract,
            ApsExpectationEmitters.M5,
            ApsExpectationGrains.Week,
            DateTime.UtcNow.Date,
            DateTime.UtcNow.Date.AddDays(7),
            "contract:test",
            """{"hre":10}""",
            Hre: 10);

        await service.EmitAsync(cmd);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.EmitAsync(cmd));

        var id2 = "exp-" + Guid.NewGuid().ToString("N");
        var replanned = await service.EmitReplanAsync(
            cmd,
            id2,
            DateTime.UtcNow.Date.AddDays(1),
            DateTime.UtcNow.Date.AddDays(8),
            causationId: id1);

        Assert.Equal(id2, replanned.ExpectedId);
        Assert.Equal(id1, replanned.CausationId);
    }

    [Fact]
    public async Task Places_are_recursive_and_stock_lot_has_bath_status()
    {
        if (!CanConnect())
        {
            return;
        }

        var bootstrap = Bootstrap();
        var repo = new SqlServerApsReferentialRepository(bootstrap);
        var service = new ApsReferentialService(repo);
        await service.EnsureReadyAsync();

        var places = await service.GetPlacesAsync();
        Assert.Contains(places, p => p.ParentId is null);
        Assert.Contains(places, p => p.ParentId is not null);

        var lots = await service.GetStockLotsAsync();
        Assert.Contains(lots, l => l.Status is "LIBRE" or "RESERVE_MTS" or "RESERVE_COMMANDE");
    }

    [Fact]
    public async Task Compiler_invalidates_and_rebuilds()
    {
        if (!CanConnect())
        {
            return;
        }

        var bootstrap = Bootstrap();
        var repo = new SqlServerApsCompilerRepository(bootstrap);
        var service = new ApsCompilerService(repo);

        var root = "APS_TEST_" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var request = new CompileApsRequest(
            root,
            ApsSegments.B,
            "CIR_TEST",
            BomFingerprint: "bom1",
            RoutingFingerprint: "rtg1",
            BomLines: [new CompileBomLineInput("C1", 1, "OP1")],
            RoutingOps: [new CompileRoutingOpInput("OP1", "REMAILLAGE", 8, true)]);

        var bundle = await service.CompileAsync(request);
        Assert.False(string.IsNullOrWhiteSpace(bundle.SourceHash));

        var list = await service.ListAsync(root, ApsCompilerStatuses.Valid);
        Assert.Equal(4, list.Count);

        await service.InvalidateAsync($"BOM:bom1|RTG:rtg1");
        var invalid = await service.ListAsync(root, ApsCompilerStatuses.Invalid);
        Assert.NotEmpty(invalid);

        await service.RebuildAsync(request with { BomFingerprint = "bom2" });
        var validAgain = await service.ListAsync(root, ApsCompilerStatuses.Valid);
        Assert.Equal(4, validAgain.Count);
        Assert.All(validAgain, a => Assert.Equal(ApsCompilerStatuses.Valid, a.Status));
    }
}
