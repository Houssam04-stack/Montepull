namespace Axioplan.GammesNomenclatures.Application.Models;

/// <summary>
/// Contrat applicatif léger de traçabilité — réutilise les identifiants de runs existants.
/// </summary>
public enum CalculationSourceType
{
    Real,
    Simulation,
    Demo
}

public sealed record CalculationRunContext
{
    public string RunId { get; init; } = string.Empty;
    public CalculationSourceType SourceType { get; init; }
    public string? DatasetVersionId { get; init; }
    public Guid? Mvp0CampaignId { get; init; }
    public int? ExistingCbnRunId { get; init; }
    public long? ExistingCapacityRunId { get; init; }
    public long? ExistingLoadRunId { get; init; }
    public string Status { get; init; } = "COMPLETED";
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public IReadOnlyList<string> BlockingErrors { get; init; } = [];

    public bool IsValidatedForProductionPlanning =>
        SourceType == CalculationSourceType.Real
        && BlockingErrors.Count == 0;
}

public sealed record CbnApplicationRunResult<T>(T Result, CalculationRunContext Context);
