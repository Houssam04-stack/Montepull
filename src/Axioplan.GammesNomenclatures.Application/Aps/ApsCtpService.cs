using System.Diagnostics;
using System.Text.Json;
using Axioplan.GammesNomenclatures.Application.Abstractions;
using Axioplan.GammesNomenclatures.Application.Models;
using Axioplan.GammesNomenclatures.Application.Mvp0;
using Axioplan.GammesNomenclatures.Domain.Aps.Ctp;
using Axioplan.GammesNomenclatures.Domain.Aps.Expectations;
using Axioplan.GammesNomenclatures.Domain.Aps.Journal;
using Microsoft.Extensions.DependencyInjection;

namespace Axioplan.GammesNomenclatures.Application.Aps;

public static class ApsCtpExtensions
{
    public static IServiceCollection AddApsCtpApplication(this IServiceCollection services)
    {
        services.AddScoped<ApsCtpService>();
        return services;
    }
}

public sealed class ApsCtpService(
    IApsCtpRepository ctpRepository,
    IApsJournalRepository journalRepository,
    IApsExpectationRepository expectationRepository,
    Mvp0ApsGateGuard gateGuard)
{
    public async Task EnsureReadyAsync(CancellationToken cancellationToken = default)
    {
        await ctpRepository.EnsureSchemaAsync(cancellationToken);
        await ctpRepository.SeedDemoAsync(cancellationToken);
    }

    public Task<IReadOnlyList<ApsCtpPromiseDto>> ListPromisesAsync(CancellationToken cancellationToken = default)
        => ctpRepository.ListPromisesAsync(cancellationToken);

    public async Task<ApsCtpEvaluateResultDto> EvaluateAsync(
        ApsCtpEvaluateRequest request,
        CancellationToken cancellationToken = default)
    {
        await EnsureReadyAsync(cancellationToken);
        var sw = Stopwatch.StartNew();
        var demand = ToDemand(request);
        var rules = await ctpRepository.LoadRegimeDefaultsAsync(cancellationToken);
        var regime = RegimeResolutionEngine.Resolve(demand, rules, DateOnly.FromDateTime(DateTime.Today));

        // Prefetch for router — BuildContext needs route; approximate with scenario seeds
        var draftCtx = await ctpRepository.BuildContextAsync(demand, regime, "R2", request.Scenario, cancellationToken);
        var route = CtpRouterEngine.Route(
            regime,
            draftCtx.PfAvailable,
            demand.Quantity,
            draftCtx.Yarn.CompatibleBathAvailable,
            draftCtx.Yarn.PurchaseRequired);

        var ctx = await ctpRepository.BuildContextAsync(
            demand, route.EffectiveRegime, route.Route, request.Scenario, cancellationToken);
        // Re-route with accurate context
        route = CtpRouterEngine.Route(
            regime,
            ctx.PfAvailable,
            demand.Quantity,
            ctx.Yarn.CompatibleBathAvailable,
            ctx.Yarn.PurchaseRequired);
        ctx = ctx with { Route = route.Route, EffectiveRegime = route.EffectiveRegime };

        var answer = CtpEngine.Evaluate(ctx, commitReservations: false);
        sw.Stop();
        return new ApsCtpEvaluateResultDto(answer, sw.ElapsedMilliseconds, true);
    }

    public async Task<(ApsCtpEvaluateResultDto Result, string? Error)> PromiseAsync(
        ApsCtpEvaluateRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await gateGuard.RequireApprovedCampaignAsync(request.SourceType, cancellationToken: cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            return (null!, ex.Message);
        }

        await EnsureReadyAsync(cancellationToken);
        var sw = Stopwatch.StartNew();
        var demand = ToDemand(request);
        var rules = await ctpRepository.LoadRegimeDefaultsAsync(cancellationToken);
        var regime = RegimeResolutionEngine.Resolve(demand, rules, DateOnly.FromDateTime(DateTime.Today));

        var ctx = await ctpRepository.BuildContextAsync(demand, regime, "R2", request.Scenario, cancellationToken);
        var route = CtpRouterEngine.Route(
            regime, ctx.PfAvailable, demand.Quantity,
            ctx.Yarn.CompatibleBathAvailable, ctx.Yarn.PurchaseRequired);
        ctx = await ctpRepository.BuildContextAsync(
            demand, route.EffectiveRegime, route.Route, request.Scenario, cancellationToken);
        route = CtpRouterEngine.Route(
            regime, ctx.PfAvailable, demand.Quantity,
            ctx.Yarn.CompatibleBathAvailable, ctx.Yarn.PurchaseRequired);
        ctx = ctx with { Route = route.Route, EffectiveRegime = route.EffectiveRegime };

        if (route.Traces.Any(t => t.Contains("bascule MTO", StringComparison.OrdinalIgnoreCase)))
        {
            await journalRepository.EnsureSchemaAsync(cancellationToken);
            await journalRepository.AppendFactAsync(new AppendApsJournalFactCommand(
                ApsFactEventTypes.RegimeArbitration,
                DateTime.UtcNow,
                demand.DemandId,
                JsonSerializer.Serialize(new { from = ApsRegimes.Mts, to = ApsRegimes.Mto, note = "R1 insuffisant" }),
                "aps-ctp"), cancellationToken);
        }

        var answer = CtpEngine.Evaluate(ctx, commitReservations: true);
        sw.Stop();

        await journalRepository.EnsureSchemaAsync(cancellationToken);
        await expectationRepository.EnsureSchemaAsync(cancellationToken);

        if (answer.Outcome == ApsCtpOutcomes.Infeasible)
        {
            await journalRepository.AppendFactAsync(new AppendApsJournalFactCommand(
                ApsFactEventTypes.EnvelopeViolated,
                DateTime.UtcNow,
                demand.DemandId,
                JsonSerializer.Serialize(answer.Cause),
                "aps-ctp"), cancellationToken);
            await journalRepository.AppendFactAsync(new AppendApsJournalFactCommand(
                ApsFactEventTypes.PromiseRejected,
                DateTime.UtcNow,
                demand.DemandId,
                JsonSerializer.Serialize(answer),
                "aps-ctp"), cancellationToken);

            // Persist refused promise for history (no reservations)
            await ctpRepository.CommitPromiseAtomicAsync(
                answer with { ReservesCreated = false }, demand, cancellationToken);

            return (new ApsCtpEvaluateResultDto(answer, sw.ElapsedMilliseconds, false), null);
        }

        var (ok, err, _) = await ctpRepository.CommitPromiseAtomicAsync(answer, demand, cancellationToken);
        if (!ok)
        {
            return (new ApsCtpEvaluateResultDto(
                answer with
                {
                    Outcome = ApsCtpOutcomes.Infeasible,
                    ReservesCreated = false,
                    Cause = answer.Cause with
                    {
                        Explanation = $"Réservation échouée — rollback complet. {err}"
                    }
                }, sw.ElapsedMilliseconds, false), err);
        }

        await journalRepository.AppendFactAsync(new AppendApsJournalFactCommand(
            ApsFactEventTypes.PromiseMade,
            DateTime.UtcNow,
            demand.DemandId,
            JsonSerializer.Serialize(answer),
            "aps-ctp"), cancellationToken);
        await journalRepository.AppendFactAsync(new AppendApsJournalFactCommand(
            ApsFactEventTypes.CapacityReserved,
            DateTime.UtcNow,
            demand.DemandId,
            JsonSerializer.Serialize(new { answer.Bottleneck, answer.ProposedDate }),
            "aps-ctp"), cancellationToken);
        await journalRepository.AppendFactAsync(new AppendApsJournalFactCommand(
            ApsFactEventTypes.MaterialReserved,
            DateTime.UtcNow,
            demand.DemandId,
            JsonSerializer.Serialize(new { answer.SelectedBath, demand.Quantity }),
            "aps-ctp"), cancellationToken);

        await EmitExpectationsAsync(answer, demand, cancellationToken);
        return (new ApsCtpEvaluateResultDto(answer with { ReservesCreated = true }, sw.ElapsedMilliseconds, false), null);
    }

    public async Task<(bool Ok, string? Error)> ReleaseAsync(long promiseId, CancellationToken cancellationToken = default)
    {
        await EnsureReadyAsync(cancellationToken);
        var (ok, err) = await ctpRepository.ReleasePromiseDemoAsync(promiseId, cancellationToken);
        if (!ok)
        {
            return (ok, err);
        }

        await journalRepository.EnsureSchemaAsync(cancellationToken);
        await journalRepository.AppendFactAsync(new AppendApsJournalFactCommand(
            ApsFactEventTypes.CapacityReleased,
            DateTime.UtcNow,
            $"promise-{promiseId}",
            """{"note":"Liberation demo — evenement compensatoire"}""",
            "aps-ctp"), cancellationToken);
        await journalRepository.AppendFactAsync(new AppendApsJournalFactCommand(
            ApsFactEventTypes.MaterialReleased,
            DateTime.UtcNow,
            $"promise-{promiseId}",
            """{"note":"Matiere liberee — evenement compensatoire"}""",
            "aps-ctp"), cancellationToken);
        return (true, null);
    }

    public async Task RejectAsync(ApsCtpEvaluateRequest request, CancellationToken cancellationToken = default)
    {
        await EnsureReadyAsync(cancellationToken);
        var demand = ToDemand(request);
        await journalRepository.EnsureSchemaAsync(cancellationToken);
        await journalRepository.AppendFactAsync(new AppendApsJournalFactCommand(
            ApsFactEventTypes.PromiseRejected,
            DateTime.UtcNow,
            demand.DemandId,
            JsonSerializer.Serialize(new { request, reason = "Refus manuel UI" }),
            "aps-ctp"), cancellationToken);
    }

    private async Task EmitExpectationsAsync(
        ApsCtpAnswer answer,
        ApsCtpDemand demand,
        CancellationToken cancellationToken)
    {
        if (answer.ProposedDate is null)
        {
            return;
        }

        var date = answer.ProposedDate.Value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        await expectationRepository.EmitAsync(new EmitApsExpectationCommand(
            $"ctp-eng-{demand.DemandId}",
            ApsExpectationTypes.ClientEngagementDeadline,
            ApsExpectationEmitters.M4,
            ApsExpectationGrains.Day,
            date.AddDays(-7),
            date,
            demand.DemandId,
            JsonSerializer.Serialize(new { demand.CustomerCode, answer.ProposedDate }),
            PlanRef: answer.CircuitCode,
            Hre: answer.Hre,
            CausationId: demand.DemandId), cancellationToken);

        await expectationRepository.EmitAsync(new EmitApsExpectationCommand(
            $"ctp-exp-{demand.DemandId}",
            ApsExpectationTypes.ShipmentExpected,
            ApsExpectationEmitters.M4,
            ApsExpectationGrains.Day,
            date,
            date.AddDays(1),
            demand.DemandId,
            JsonSerializer.Serialize(new { answer.ProposedDate }),
            PlanRef: answer.CircuitCode,
            Hre: answer.Hre,
            CausationId: demand.DemandId), cancellationToken);

        if (answer.ExternalEngagementCode is not null)
        {
            await expectationRepository.EmitAsync(new EmitApsExpectationCommand(
                $"ctp-rem-{demand.DemandId}",
                ApsExpectationTypes.ExternalHandoverExpected,
                ApsExpectationEmitters.M5,
                ApsExpectationGrains.Day,
                date.AddDays(-10),
                date.AddDays(-5),
                demand.DemandId,
                JsonSerializer.Serialize(new { answer.ExternalEngagementCode }),
                CausationId: demand.DemandId), cancellationToken);
            await expectationRepository.EmitAsync(new EmitApsExpectationCommand(
                $"ctp-ret-{demand.DemandId}",
                ApsExpectationTypes.ExternalReturnExpected,
                ApsExpectationEmitters.M5,
                ApsExpectationGrains.Day,
                date,
                date.AddDays(2),
                demand.DemandId,
                JsonSerializer.Serialize(new { answer.ExternalEngagementCode }),
                CausationId: demand.DemandId), cancellationToken);
        }
    }

    private static ApsCtpDemand ToDemand(ApsCtpEvaluateRequest r)
        => new(
            "DEM-" + (r.OrderReference ?? Guid.NewGuid().ToString("N")[..8]),
            r.ArticleCode,
            r.CustomerCode,
            r.ArticleCategory ?? "PULL",
            r.CustomerCategory ?? "STD",
            r.Quantity,
            r.DesiredDate,
            r.CustomerMaxDate,
            r.Margin,
            r.Priority,
            r.DeclaredRegime,
            r.QuantityTolerance,
            r.OrderReference);
}
