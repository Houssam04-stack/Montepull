using Axioplan.GammesNomenclatures.Application.Models;
using Axioplan.GammesNomenclatures.Domain.Aps.Contracts;

namespace Axioplan.GammesNomenclatures.Application.Abstractions;

public interface IApsPhase9Repository
{
    Task EnsureSchemaAsync(CancellationToken cancellationToken = default);
    Task SeedDemoAsync(CancellationToken cancellationToken = default);

    Task<ApsBarrierPolicy> GetBarrierPolicyAsync(CancellationToken cancellationToken = default);
    Task<ApsInertiaWeights> GetInertiaWeightsAsync(CancellationToken cancellationToken = default);

    Task<ApsFlowContract> SaveContractAsync(ApsFlowContract contract, CancellationToken cancellationToken = default);
    Task<ApsFlowContract?> GetLatestContractAsync(string contractId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ApsFlowContractDto>> ListContractsAsync(CancellationToken cancellationToken = default);

    Task SavePlanSnapshotAsync(
        string grain,
        string roleOrPost,
        DateOnly dayOrWeekStart,
        string payloadJson,
        string planRef,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<(string Grain, string RoleOrPost, DateOnly Start, string PayloadJson, string PlanRef)>> ListPlanProjectionsAsync(
        string? grain,
        CancellationToken cancellationToken = default);

    Task UpsertEstimatorAsync(ApsEstimatorState state, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ApsEstimatorState>> ListEstimatorsAsync(CancellationToken cancellationToken = default);
    Task SaveRecalibrationAsync(ApsRecalibrationResult result, CancellationToken cancellationToken = default);

    Task<(bool Ok, string Hash, string Status)> InvalidateAndRecompileDemoAsync(
        string rootArticle,
        bool forceFail,
        CancellationToken cancellationToken = default);

    Task<long> StartNightlyRunAsync(CancellationToken cancellationToken = default);
    Task AddNightlyStepAsync(long runId, int order, string name, string status, string detail, CancellationToken cancellationToken = default);
    Task FinishNightlyRunAsync(long runId, string status, string? notes, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ApsNightlyRunDto>> ListNightlyRunsAsync(CancellationToken cancellationToken = default);

    Task SaveMatchesAsync(IReadOnlyList<ApsExpectationFactMatch> matches, CancellationToken cancellationToken = default);
    Task SaveReplayAsync(ApsReplayValidationResult result, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ApsReplayDto>> ListReplaysAsync(CancellationToken cancellationToken = default);

    Task SaveNervousnessAsync(DateOnly from, DateOnly to, ApsNervousnessMetrics metrics, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ApsNervousnessDto>> ListNervousnessAsync(CancellationToken cancellationToken = default);

    Task<ApsReplayValidationResult?> GetLatestRecipeAsync(CancellationToken cancellationToken = default);
}
