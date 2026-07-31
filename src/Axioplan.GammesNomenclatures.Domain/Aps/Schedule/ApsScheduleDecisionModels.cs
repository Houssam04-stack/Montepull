namespace Axioplan.GammesNomenclatures.Domain.Aps.Schedule;

public static class ApsScheduleTaskKinds
{
    public const string WorkOrder = "OF";
    public const string Operation = "OPERATION";
    public const string ResourceBucket = "RESSOURCE";
}

public static class ApsScheduleTaskStatuses
{
    public const string Planned = "PLANIFIE";
    public const string InProgress = "EN_COURS";
    public const string Late = "RETARD";
    public const string Saturated = "SATURE";
    public const string Done = "TERMINE";
    public const string External = "EXTERNE";
}

/// <summary>Tâche exploitable pour Gantt / exports (dérivée du planning APS, pas une estimation isolée).</summary>
public sealed record ApsScheduleTask(
    string TaskId,
    string Kind,
    string Label,
    string? ParentTaskId,
    string? ResourceCode,
    string? ArticleCode,
    DateOnly Start,
    DateOnly End,
    double Quantity,
    double Charge,
    string Status,
    bool IsBottleneck,
    bool IsOnCriticalPath,
    IReadOnlyList<string> DependsOnTaskIds,
    string? OrderId = null);

/// <summary>Aide à la décision : durées cohérentes avec les buckets APS.</summary>
public sealed record ApsScheduleDurationSummary(
    DateOnly StartDate,
    DateOnly EstimatedEndDate,
    int WorkingDays,
    int CalendarDays,
    int? MarginWorkingDays,
    DateOnly? NeedDate,
    double? MaxLoadRate,
    string? BottleneckResourceCode,
    string BottleneckExplanation,
    IReadOnlyList<string> CriticalPathTaskIds,
    IReadOnlyList<string> Traces);

public sealed record ApsScheduleDecisionResult(
    ApsScheduleDurationSummary Duration,
    IReadOnlyList<ApsScheduleTask> Tasks,
    DateOnly Today,
    DateOnly WindowFrom,
    DateOnly WindowTo);
