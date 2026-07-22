namespace Axioplan.GammesNomenclatures.Application.Models;

public sealed record FamilyListItem(string Code, string Label);

public sealed record ProfileListItem(
    string Code,
    string Label,
    string Status,
    bool CanGenerateBom,
    bool CanGenerateRouting);

public sealed record SimulationRequest(
    string FamilyCode,
    string ProfileCode,
    IReadOnlyList<string> Sizes,
    IReadOnlyList<string> Colors);

public sealed record FamilyInfo(
    string Code,
    string Label,
    string? BomCode,
    string? RoutingCode,
    string BaseArticleCode);

public sealed record ProfileInfo(
    string Code,
    string Label,
    string Status,
    bool CanGenerateBom,
    bool CanGenerateRouting);

public sealed record VariantResult(
    int Index,
    string ArticleCode,
    string ShortCode,
    string SizeCode,
    string ColorCode);

public sealed record BomSummaryRow(
    string VariantShortCode,
    string ComponentCode,
    double QuantityNet,
    double QuantityGross,
    string Unit);

public sealed record ComponentTotal(
    string ComponentCode,
    double TotalNet,
    double TotalGross,
    string Unit);

public sealed record RoutingSummaryRow(
    string VariantShortCode,
    double TotalTime,
    string TopWorkcentersSummary);

public sealed record CalculationTraceResult(
    string ObjectType,
    string VariantShortCode,
    string RuleCode,
    string SourceLabel,
    double BaseValue,
    double SizeCoefficient,
    double ColorCoefficient,
    double LossRate,
    double ResultValue,
    string Unit,
    string Formula);

public sealed record SimProductInfo(
    string Code,
    string Designation,
    string BomLabel,
    string RoutingLabel);

public sealed record BomBaseRow(
    string ComponentCode,
    string ComponentDesignation,
    double QuantityNet,
    double QuantityGross,
    string Unit);

public sealed record RoutingOperationDetailRow(
    int OperationNumber,
    string OperationName,
    string WorkCenter,
    double Quantity,
    double TimeMinutes);

public sealed record GammesSimulationRequest(
    int SimulationId,
    int TemplateArticleId,
    IReadOnlyList<SizeCoefficientInput> Sizes,
    IReadOnlyList<ColorCoefficientInput> Colors);

public sealed record SimulationResult
{
    public string Mode { get; init; } = "SIMULATION";

    // Ancien mode (product_families) — conserve pour compatibilite CBN.
    public FamilyInfo? Family { get; init; }
    public ProfileInfo? Profile { get; init; }
    public bool ProfileCanGenerate { get; init; }
    public IReadOnlyList<VariantResult> Variants { get; init; } = [];
    public IReadOnlyList<BomSummaryRow> BomSummary { get; init; } = [];
    public IReadOnlyList<ComponentTotal> ComponentTotals { get; init; } = [];
    public IReadOnlyList<RoutingSummaryRow> RoutingSummary { get; init; } = [];
    public IReadOnlyList<CalculationTraceResult> Traces { get; init; } = [];

    // Mode Simulation MRP (gammes & nomenclatures).
    public SimProductInfo? Product { get; init; }
    public int VariantCount { get; init; }
    public int OperationCount { get; init; }
    public string TimeUnit { get; init; } = "N/A";
    public double TotalRoutingTimeMinutes { get; init; }
    public IReadOnlyList<string> SelectedSizes { get; init; } = [];
    public IReadOnlyList<string> SelectedColors { get; init; } = [];
    public IReadOnlyList<BomBaseRow> BaseBom { get; init; } = [];
    public IReadOnlyList<RoutingOperationDetailRow> RoutingOperations { get; init; } = [];
}
