namespace Axioplan.GammesNomenclatures.Domain.Simulation;

/// <summary>
/// Entrees domaine pour le moteur CBN/MRP de simulation (pures, sans SQL).
/// </summary>
public sealed record SimArticleInput(
    int Id,
    string Code,
    string Designation,
    string ArticleType,
    string ProcurementType,
    int LeadTimeDays,
    string? Size,
    string? Color);

public sealed record SimBomLineInput(
    int Id,
    int ParentArticleId,
    int ComponentArticleId,
    double QuantityPer,
    double ScrapRate,
    int OffsetDays);

public sealed record SimCbnParameterInput(
    int ArticleId,
    double OnHandStock,
    double SafetyStock,
    double ReservedQuantity,
    double ScheduledReceiptProduction,
    double ScheduledReceiptPurchase,
    int LeadTimeDays,
    string LotRule,
    double MinLot,
    double MultipleLot);

public sealed record SimSalesOrderInput(
    int Id,
    string CustomerOrderNumber,
    int ArticleId,
    double Quantity,
    DateOnly RequestedDate);

/// <summary>
/// Ligne de nomenclature aplatie CONTROLEE (vue de calcul, pas remplacement de la BOM multi-niveaux).
/// Conserve parent, composant, niveau, date et chemin de tracabilite.
/// </summary>
public sealed record ControlledFlatBomLine(
    int RootArticleId,
    int? ParentArticleId,
    string? ParentArticleCode,
    int ComponentArticleId,
    string ComponentArticleCode,
    int Level,
    double QuantityPer,
    double CumulativeQuantity,
    DateOnly NeedDate,
    string Path,
    int OffsetDays);

public sealed record GrossRequirementResult(
    int ArticleId,
    string ArticleCode,
    int? ParentArticleId,
    int Level,
    double GrossQuantity,
    DateOnly NeedDate,
    string SourceType,
    string? Path);

public sealed record NetRequirementResult(
    int ArticleId,
    string ArticleCode,
    double GrossRequirement,
    double OnHandStock,
    double SafetyStock,
    double ReservedQuantity,
    double ScheduledReceiptProduction,
    double ScheduledReceiptPurchase,
    double NetRequirement,
    DateOnly NeedDate);

/// <summary>
/// POR = ReleaseDate (lancement). PORec = ReceiptDate (reception / couverture du besoin).
/// </summary>
public sealed record PlannedOrderResult(
    int ArticleId,
    string ArticleCode,
    string OrderType,
    double Quantity,
    DateOnly NeedDate,
    DateOnly ReleaseDate,
    DateOnly ReceiptDate,
    string SourceType,
    string Justification);

public sealed record WorkOrderResult(
    int ArticleId,
    string ArticleCode,
    double Quantity,
    DateOnly StartDate,
    DateOnly EndDate,
    string Status);

public sealed record PeggingNodeResult(
    int ComponentArticleId,
    string ArticleCode,
    int? ParentArticleId,
    int Level,
    double Quantity,
    DateOnly NeedDate,
    string SourceType,
    string Path,
    IReadOnlyList<PeggingNodeResult> Children);

public sealed record CbnAlertResult(
    int? ArticleId,
    string AlertType,
    string Message,
    string Severity);

public sealed record SimulationCbnRunResult(
    IReadOnlyDictionary<int, int> LowLevelCodes,
    IReadOnlyList<ControlledFlatBomLine> FlatBomLines,
    IReadOnlyList<GrossRequirementResult> GrossRequirements,
    IReadOnlyList<NetRequirementResult> NetRequirements,
    IReadOnlyList<PlannedOrderResult> PlannedOrders,
    IReadOnlyList<WorkOrderResult> WorkOrders,
    IReadOnlyList<PeggingNodeResult> PeggingTree,
    IReadOnlyList<CbnAlertResult> Alerts);
