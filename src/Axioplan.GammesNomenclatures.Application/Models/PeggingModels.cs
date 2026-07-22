namespace Axioplan.GammesNomenclatures.Application.Models;

public sealed record PeggingRunRequest(int CbnRunId);

public sealed record PeggingCoverageRow(
    int RequirementId,
    string VariantLabel,
    string ComponentCode,
    string ComponentLabel,
    string ComponentType,
    double RequiredQuantity,
    double CoveredByStock,
    double CoveredByManufacturing,
    double CoveredByPurchase,
    double RemainingToCover,
    string Unit,
    string CoverageStatus,
    string SourcePath);

public sealed record PeggingSupplyUsageRow(
    string SupplyEntityType,
    int SupplyEntityId,
    string SupplyLabel,
    string ComponentCode,
    double AvailableQuantity,
    double PeggedQuantity,
    double RemainingQuantity,
    string Unit,
    string UsageStatus);

public sealed record PeggingLinkHistoryRow(
    int PeggingRunId,
    int PeggingRunVersionNo,
    int LinkId,
    int VersionNo,
    double Quantity,
    string? ChangeReason,
    DateTime ChangedAt);

public sealed record PeggingLinkRow(
    int Id,
    string LinkType,
    string Direction,
    string SourceEntityType,
    int SourceEntityId,
    string SourceLabel,
    string TargetEntityType,
    int TargetEntityId,
    string TargetLabel,
    double Quantity,
    string Unit,
    string? SourcePath,
    int VersionNo);

public sealed record PeggingRunResult
{
    public int PeggingRunId { get; init; }
    public int CbnRunId { get; init; }
    public int VersionNo { get; init; }
    public string Status { get; init; } = "COMPLETED";
    public IReadOnlyList<PeggingLinkRow> Links { get; init; } = [];
    public IReadOnlyList<PeggingCoverageRow> Coverage { get; init; } = [];
    public IReadOnlyList<PeggingSupplyUsageRow> SupplyUsage { get; init; } = [];
    public IReadOnlyList<PeggingLinkHistoryRow> History { get; init; } = [];
}

public sealed record RoutingConsultationRow(
    int OperationNo,
    string Name,
    string WorkcenterCode,
    double QuantityBase,
    double TimeBase,
    string TimeUnit,
    string Behavior);

public sealed record BomConsultationRow(
    int LineNo,
    string ComponentCode,
    string ComponentLabel,
    double QuantityBase,
    string Unit,
    double LossRate,
    string Behavior);

public sealed record FlattenedBomConsultationRow(
    int CbnRunId,
    string ComponentCode,
    string ComponentLabel,
    int BomLevel,
    double QuantityPerUnit,
    string Unit,
    string SourcePath);

public sealed record CbnRunListItem(int Id, string SalesOrderCode, string FamilyCode, string Status, DateTime CreatedAt);
