using System.Globalization;
using System.Text.Json;
using Axioplan.GammesNomenclatures.Application.Abstractions;
using Axioplan.GammesNomenclatures.Application.Models;
using Axioplan.GammesNomenclatures.Domain.Aps.Contracts;
using Axioplan.GammesNomenclatures.Domain.Aps.Journal;
using Microsoft.Extensions.DependencyInjection;

namespace Axioplan.GammesNomenclatures.Application.Aps;

public static class ApsPhase9Extensions
{
    public static IServiceCollection AddApsPhase9Application(this IServiceCollection services)
    {
        services.AddScoped<ApsContractService>();
        services.AddScoped<NightlyCycleService>();
        services.AddScoped<ApsRecipeService>();
        return services;
    }
}

public sealed class ApsContractService(
    IApsPhase9Repository repository,
    IApsJournalRepository journalRepository)
{
    public async Task EnsureReadyAsync(CancellationToken cancellationToken = default)
    {
        await repository.EnsureSchemaAsync(cancellationToken);
        await repository.SeedDemoAsync(cancellationToken);
    }

    public Task<IReadOnlyList<ApsFlowContractDto>> ListContractsAsync(CancellationToken cancellationToken = default)
        => repository.ListContractsAsync(cancellationToken);

    public Task<ApsBarrierPolicy> GetBarrierPolicyAsync(CancellationToken cancellationToken = default)
        => repository.GetBarrierPolicyAsync(cancellationToken);

    public async Task<ApsFlowContract> PublishDemoAsync(CancellationToken cancellationToken = default)
    {
        await EnsureReadyAsync(cancellationToken);
        var today = DateOnly.FromDateTime(DateTime.Today);
        var week = ISOWeek.GetWeekOfYear(DateTime.Today);
        var year = DateTime.Today.Year;
        var monday = today.AddDays(-(int)(today.DayOfWeek == DayOfWeek.Sunday ? 6 : today.DayOfWeek - DayOfWeek.Monday));
        var draft = new ApsFlowContractDraft(
            week, year, EngagedLoad: 120,
            BottleneckUnit: "HEURES_PERSONNE",
            monday.AddDays(7), monday.AddDays(13),
            [new ApsCompositionShare("DEMO_PF", 0.6), new ApsCompositionShare("PULL", 0.4)],
            [new ApsResourceEnvelope("CC_REMAILLAGE", 40)],
            "DEMO_PF", "PULL", null, "PLAN-DEMO-P9", "seed-simule");
        var contract = FlowContractEngine.CreateDraft(draft, "FC-DEMO", DateTime.UtcNow);
        contract = FlowContractEngine.Publish(contract, DateTime.UtcNow);
        await repository.SaveContractAsync(contract, cancellationToken);
        await journalRepository.EnsureSchemaAsync(cancellationToken);
        await journalRepository.AppendFactAsync(new AppendApsJournalFactCommand(
            ApsFactEventTypes.ContractEngaged,
            DateTime.UtcNow,
            contract.ContractId,
            JsonSerializer.Serialize(contract),
            "aps-contracts"), cancellationToken);

        await repository.SavePlanSnapshotAsync(
            "SEMAINE", "HEBDO", contract.WindowStart,
            JsonSerializer.Serialize(new { contract.EngagedLoad, contract.Composition, contract.WindowStart, contract.WindowEnd, Hre = 18.0 }),
            contract.PlanRef, cancellationToken);
        await repository.SavePlanSnapshotAsync(
            "JOUR_POSTE", "CC_REMAILLAGE", contract.WindowStart,
            JsonSerializer.Serialize(new { Sequence = new[] { "OF-1", "OF-2", "OF-3" }, Note = "Seul poste sequencé" }),
            contract.PlanRef, cancellationToken);
        await repository.SavePlanSnapshotAsync(
            "JOUR", "CC_TRICOTAGE", contract.WindowStart,
            JsonSerializer.Serialize(new { Volume = 80, Mix = "DEMO", Note = "Aucun ordre a la minute" }),
            contract.PlanRef, cancellationToken);
        await repository.SavePlanSnapshotAsync(
            "ROLE", "ACHETEUR_FIL", contract.WindowStart,
            JsonSerializer.Serialize(new { Actions = new[] { "Confirmer achat fil S+2" } }),
            contract.PlanRef, cancellationToken);

        return contract;
    }

    public async Task<(ApsFlowContract? Revised, string? Error)> TryReviseAsync(
        string contractId,
        double newLoad,
        double expectedGain,
        string? justification,
        int planWeekOffset,
        CancellationToken cancellationToken = default)
    {
        await EnsureReadyAsync(cancellationToken);
        var current = await repository.GetLatestContractAsync(contractId, cancellationToken)
                      ?? throw new InvalidOperationException("Contrat introuvable.");
        var policy = await repository.GetBarrierPolicyAsync(cancellationToken);
        var today = DateOnly.FromDateTime(DateTime.Today);
        var asOfMonday = today.AddDays(-(int)(today.DayOfWeek == DayOfWeek.Sunday ? 6 : today.DayOfWeek - DayOfWeek.Monday));
        var planMonday = asOfMonday.AddDays(7 * planWeekOffset);
        var zone = BarrierPolicyEngine.ResolveZone(planMonday, asOfMonday, policy);
        var weights = await repository.GetInertiaWeightsAsync(cancellationToken);
        var inertia = InertiaCostEngine.Assess(
            zone, weights,
            dateDeltaDays: 2,
            loadDeltaAbs: Math.Abs(newLoad - current.EngagedLoad),
            compositionDeltaAbs: 0.1,
            circuitChanged: false,
            sequenceChanged: false,
            rePromise: true,
            reservationChurn: true,
            expectedGain,
            justification);

        try
        {
            BarrierPolicyEngine.AssertModificationAllowed(zone, inertia);
        }
        catch (Exception ex)
        {
            return (null, ex.Message + " | " + inertia.Explanation);
        }

        var draft = new ApsFlowContractDraft(
            current.IsoWeek, current.Year, newLoad, current.BottleneckUnit,
            current.WindowStart, current.WindowEnd, current.Composition, current.Envelopes,
            current.ArticleCode, current.FamilyCode, current.OrderCode, current.PlanRef, "revise-simule");
        var revised = FlowContractEngine.Revise(current, draft, DateTime.UtcNow);
        revised = FlowContractEngine.Publish(revised, DateTime.UtcNow);
        await repository.SaveContractAsync(revised, cancellationToken);
        await journalRepository.AppendFactAsync(new AppendApsJournalFactCommand(
            ApsFactEventTypes.ContractRevised,
            DateTime.UtcNow,
            revised.ContractId,
            JsonSerializer.Serialize(new { revised, inertia }),
            "aps-contracts"), cancellationToken);
        return (revised, null);
    }

    public Task<IReadOnlyList<(string Grain, string RoleOrPost, DateOnly Start, string PayloadJson, string PlanRef)>> ListPlansAsync(
        string? grain,
        CancellationToken cancellationToken = default)
        => repository.ListPlanProjectionsAsync(grain, cancellationToken);
}

public sealed class NightlyCycleService(
    IApsPhase9Repository repository,
    IApsJournalRepository journalRepository)
{
    public async Task EnsureReadyAsync(CancellationToken cancellationToken = default)
    {
        await repository.EnsureSchemaAsync(cancellationToken);
        await repository.SeedDemoAsync(cancellationToken);
    }

    public Task<IReadOnlyList<ApsNightlyRunDto>> ListRunsAsync(CancellationToken cancellationToken = default)
        => repository.ListNightlyRunsAsync(cancellationToken);

    public async Task<ApsNightlyRunDto> RunAsync(
        bool forceRecalInvalidation = false,
        bool forceRecompileFail = false,
        CancellationToken cancellationToken = default)
    {
        await EnsureReadyAsync(cancellationToken);
        await journalRepository.EnsureSchemaAsync(cancellationToken);
        var runId = await repository.StartNightlyRunAsync(cancellationToken);
        await journalRepository.AppendFactAsync(new AppendApsJournalFactCommand(
            ApsFactEventTypes.NightlyCycleStarted,
            DateTime.UtcNow,
            $"nightly-{runId}",
            "{}",
            "aps-m7"), cancellationToken);

        var stepStatus = new List<(int, string, string, string)>();
        var overall = ApsNightlyStatuses.Success;
        var notes = new List<string>();

        async Task Step(int order, string name, Func<Task<(string status, string detail)>> action)
        {
            try
            {
                var (st, detail) = await action();
                stepStatus.Add((order, name, st, detail));
                await repository.AddNightlyStepAsync(runId, order, name, st, detail, cancellationToken);
                if (st == ApsNightlyStatuses.Failed)
                {
                    overall = ApsNightlyStatuses.Failed;
                }
                else if (st == ApsNightlyStatuses.Partial && overall != ApsNightlyStatuses.Failed)
                {
                    overall = ApsNightlyStatuses.Partial;
                }
            }
            catch (Exception ex)
            {
                overall = ApsNightlyStatuses.Failed;
                stepStatus.Add((order, name, ApsNightlyStatuses.Failed, ex.Message));
                await repository.AddNightlyStepAsync(runId, order, name, ApsNightlyStatuses.Failed, ex.Message, cancellationToken);
            }
        }

        // 1 Replay faits triés occurred_at
        await Step(1, NightlyCycleSteps.Ordered[0], async () =>
        {
            var facts = await journalRepository.QueryFactsAsync(new ApsJournalQuery(Take: 50), cancellationToken);
            var ordered = facts.OrderBy(f => f.OccurredAtUtc).ThenBy(f => f.EventId).ToList();
            return (ApsNightlyStatuses.Success, $"Replay {ordered.Count} faits triés par occurred_at (deterministe).");
        });

        // 2 Projeter états
        await Step(2, NightlyCycleSteps.Ordered[1], () =>
            Task.FromResult((ApsNightlyStatuses.Success,
                "Projection SIMULEE stocks/WIP/buffer/charge réalisée/rho réalisé.")));

        // 3-4 Recalibration
        var invalidating = false;
        await Step(3, NightlyCycleSteps.Ordered[2], async () =>
        {
            var estimators = await repository.ListEstimatorsAsync(cancellationToken);
            var eta = estimators.FirstOrDefault(e => e.Code == "eta_disponibilite");
            var obs = forceRecalInvalidation ? 0.5 : 0.89;
            var result = RecalibrationEngine.ApplyEwma(
                "eta_disponibilite", eta?.Value ?? 0.9, obs, eta?.ObservationCount ?? 3,
                alpha: 0.3, invalidationThreshold: 0.15,
                "journal-faits-simule", DateTime.UtcNow);
            await repository.SaveRecalibrationAsync(result, cancellationToken);
            await repository.UpsertEstimatorAsync(new ApsEstimatorState(
                result.EstimatorCode, result.NewValue, result.ObservationCount, result.Source,
                result.AsOfUtc, result.Confidence, result.Status), cancellationToken);
            invalidating = result.InvalidatesArtifacts;
            return (ApsNightlyStatuses.Success, result.Reason);
        });

        await Step(4, NightlyCycleSteps.Ordered[3], async () =>
        {
            if (!invalidating)
            {
                return (ApsNightlyStatuses.Success, "Sous seuil — pas de CoefficientRecalibre.");
            }

            await journalRepository.AppendFactAsync(new AppendApsJournalFactCommand(
                ApsFactEventTypes.CoefficientRecalibrated,
                DateTime.UtcNow,
                "eta_disponibilite",
                """{"estimator":"eta_disponibilite"}""",
                "aps-m7"), cancellationToken);
            return (ApsNightlyStatuses.Success, "CoefficientRecalibre émis.");
        });

        var artifactsValid = true;
        await Step(5, NightlyCycleSteps.Ordered[4], async () =>
        {
            if (!invalidating)
            {
                return (ApsNightlyStatuses.Success, "Pas d'invalidation artefacts.");
            }

            await journalRepository.AppendFactAsync(new AppendApsJournalFactCommand(
                ApsFactEventTypes.CompiledInvalidated,
                DateTime.UtcNow,
                "DEMO_PF",
                """{"reason":"estimateur eta"}""",
                "aps-m7"), cancellationToken);
            return (ApsNightlyStatuses.Success, "Artefacts dépendants marqués INVALID (demo index).");
        });

        await Step(6, NightlyCycleSteps.Ordered[5], async () =>
        {
            if (!invalidating)
            {
                return (ApsNightlyStatuses.Success, "Recompilation non requise.");
            }

            var (ok, hash, status) = await repository.InvalidateAndRecompileDemoAsync(
                "DEMO_PF", forceRecompileFail, cancellationToken);
            if (!ok)
            {
                artifactsValid = false;
                notes.Add("Recompilation échouée — CBN non relancé, plan engagé conservé.");
                return (ApsNightlyStatuses.Partial, $"Recompilation FAILED status={status}");
            }

            await journalRepository.AppendFactAsync(new AppendApsJournalFactCommand(
                ApsFactEventTypes.CompileRecompiled,
                DateTime.UtcNow,
                "DEMO_PF",
                JsonSerializer.Serialize(new { hash, status }),
                "aps-m7"), cancellationToken);
            artifactsValid = string.Equals(status, "VALID", StringComparison.OrdinalIgnoreCase);
            return (ApsNightlyStatuses.Success, $"Recompilé hash={hash} status={status}");
        });

        await Step(7, NightlyCycleSteps.Ordered[6], () =>
        {
            if (!artifactsValid)
            {
                return Task.FromResult((ApsNightlyStatuses.Partial,
                    "CBN/charge NON relancés — artefact invalide (I-comp)."));
            }

            return Task.FromResult((ApsNightlyStatuses.Success,
                "CBN + charge relancés sur artefacts VALID uniquement (simulation)."));
        });

        await Step(8, NightlyCycleSteps.Ordered[7], async () =>
        {
            var policy = await repository.GetBarrierPolicyAsync(cancellationToken);
            return (ApsNightlyStatuses.Success,
                $"Replanification hors zone GELEE sous inertie (frozen S+{policy.FrozenWeeksStart}..S+{policy.FrozenWeeksEnd}).");
        });

        await Step(9, NightlyCycleSteps.Ordered[8], async () =>
        {
            await journalRepository.AppendFactAsync(new AppendApsJournalFactCommand(
                ApsFactEventTypes.PlanPublished,
                DateTime.UtcNow,
                $"plan-{runId}",
                """{"grain":"SEMAINE"}""",
                "aps-m7"), cancellationToken);
            return (ApsNightlyStatuses.Success, "PlanPublie émis.");
        });

        await Step(10, NightlyCycleSteps.Ordered[9], async () =>
        {
            var facts = await journalRepository.QueryFactsAsync(new ApsJournalQuery(Take: 100), cancellationToken);
            var nerv = NervousnessEngine.Compute(
                planDelta: 0.12,
                rePromiseRate: facts.Count(f => f.EventType == ApsFactEventTypes.PromiseRescheduled) / 10.0,
                resequenceRate: 0.05,
                circuitReassignRate: 0.02,
                bottleneckMoves: facts.Count(f => f.EventType == ApsFactEventTypes.BottleneckMoved),
                envelopeViolations: facts.Count(f => f.EventType == ApsFactEventTypes.EnvelopeViolated),
                regimeArbitrations: facts.Count(f => f.EventType == ApsFactEventTypes.RegimeArbitration),
                daysInPeriod: 7);
            var today = DateOnly.FromDateTime(DateTime.Today);
            await repository.SaveNervousnessAsync(today.AddDays(-7), today, nerv, cancellationToken);
            return (ApsNightlyStatuses.Success, $"Nervosité composite={nerv.CompositeIndex:F3}");
        });

        // Verify step order
        var names = stepStatus.OrderBy(s => s.Item1).Select(s => s.Item2).ToList();
        if (!names.SequenceEqual(NightlyCycleSteps.Ordered))
        {
            overall = ApsNightlyStatuses.Failed;
            notes.Add("Ordre des étapes nocturnes violé.");
        }

        await repository.FinishNightlyRunAsync(runId, overall, string.Join(" | ", notes), cancellationToken);
        await journalRepository.AppendFactAsync(new AppendApsJournalFactCommand(
            ApsFactEventTypes.NightlyCycleFinished,
            DateTime.UtcNow,
            $"nightly-{runId}",
            JsonSerializer.Serialize(new { overall, notes }),
            "aps-m7"), cancellationToken);

        var runs = await repository.ListNightlyRunsAsync(cancellationToken);
        return runs.First(r => r.Id == runId);
    }
}

public sealed class ApsRecipeService(
    IApsPhase9Repository repository,
    IApsJournalRepository journalRepository)
{
    public async Task EnsureReadyAsync(CancellationToken cancellationToken = default)
    {
        await repository.EnsureSchemaAsync(cancellationToken);
        await repository.SeedDemoAsync(cancellationToken);
    }

    public Task<IReadOnlyList<ApsReplayDto>> ListAsync(CancellationToken cancellationToken = default)
        => repository.ListReplaysAsync(cancellationToken);

    public Task<IReadOnlyList<ApsNervousnessDto>> ListNervousnessAsync(CancellationToken cancellationToken = default)
        => repository.ListNervousnessAsync(cancellationToken);

    public async Task<ApsReplayValidationResult> EvaluateAsync(
        bool withPriorPlan,
        bool withSampleMatches,
        CancellationToken cancellationToken = default)
    {
        await EnsureReadyAsync(cancellationToken);
        List<ApsExpectationFactMatch>? matches = null;
        if (withSampleMatches)
        {
            var detected = DateTime.UtcNow;
            matches = ExpectationFactMatcher.Match(
                [
                    ("EXP-1", ApsDeviationDimensions.Date, detected.AddDays(-3), detected.AddDays(-1), 100, "MIX-A"),
                    ("EXP-2", ApsDeviationDimensions.Composition, detected.AddDays(-2), detected.AddDays(-1), 50, "MIX-A")
                ],
                [
                    (101, "ExpeditionAttendue", detected.AddDays(-1).AddHours(2), 100, "MIX-B"),
                    (102, "MouvementStock", detected.AddHours(-1), 10, "MIX-X")
                ],
                detected).ToList();
            await repository.SaveMatchesAsync(matches, cancellationToken);
            foreach (var m in matches)
            {
                await journalRepository.EnsureSchemaAsync(cancellationToken);
                await journalRepository.AppendFactAsync(new AppendApsJournalFactCommand(
                    ApsFactEventTypes.DeviationObserved,
                    DateTime.UtcNow,
                    m.ExpectedId ?? $"evt-{m.EventId}",
                    JsonSerializer.Serialize(m),
                    "aps-recette"), cancellationToken);
            }
        }

        var result = ReplayValidationEngine.Evaluate(matches, withPriorPlan);
        await repository.SaveReplayAsync(result, cancellationToken);
        await journalRepository.EnsureSchemaAsync(cancellationToken);
        await journalRepository.AppendFactAsync(new AppendApsJournalFactCommand(
            ApsFactEventTypes.RecipeEvaluated,
            DateTime.UtcNow,
            $"recipe-{DateTime.UtcNow:yyyyMMddHHmmss}",
            JsonSerializer.Serialize(result),
            "aps-recette"), cancellationToken);
        return result;
    }

    public async Task AssertM2M6LockedAsync(CancellationToken cancellationToken = default)
    {
        var latest = await repository.GetLatestRecipeAsync(cancellationToken)
                     ?? ReplayValidationEngine.Evaluate(null, false);
        ReplayValidationEngine.AssertM2M6Locked(latest);
    }
}
