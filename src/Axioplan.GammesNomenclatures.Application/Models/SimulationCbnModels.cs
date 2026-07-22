using Axioplan.GammesNomenclatures.Application.SimulationCbn;

namespace Axioplan.GammesNomenclatures.Application.Models;

// --- Entites / DTO front Simulation CBN ---

public sealed record SimulationHeaderDto(
    int Id,
    string Name,
    string? Description,
    DateTime CreatedAt,
    string CreatedBy,
    string Status,
    bool IsActive);

public sealed record SimulationArticleDto(
    int Id,
    int SimulationId,
    int? BaseArticleId,
    string Code,
    string Designation,
    string ArticleType,
    string? Size,
    string? Color,
    bool IsTemplate,
    bool IsGenerated,
    string ProcurementType,
    int LeadTimeDays,
    string Unit,
    int? Llc);

public sealed record SimulationSalesOrderDto(
    int Id,
    int SimulationId,
    string CustomerName,
    string CustomerOrderNumber,
    int ArticleId,
    string ArticleCode,
    double Quantity,
    DateOnly RequestedDate,
    int Priority);

public sealed record SimulationBomLineDto(
    int Id,
    int ParentArticleId,
    string ParentCode,
    int ComponentArticleId,
    string ComponentCode,
    double QuantityPer,
    double ScrapRate,
    int OffsetDays,
    bool ApplySizeCoefficient,
    bool ApplyColorSubstitution,
    string ComponentUnit,
    string NomenclatureType,
    int Alternative);

public sealed record SimulationRoutingOpDto(
    int Id,
    int ArticleId,
    string ArticleCode,
    int OperationNumber,
    string OperationName,
    string? WorkCenter,
    double SetupTimeMinutes,
    double RunTimeMinutes,
    bool ApplySizeCoefficient,
    bool ApplyColorCoefficient);

public sealed record SimulationCbnParameterDto(
    int Id,
    int ArticleId,
    string ArticleCode,
    double OnHandStock,
    double SafetyStock,
    double ReservedQuantity,
    double ScheduledReceiptProduction,
    double ScheduledReceiptPurchase,
    int LeadTimeDays,
    string LotRule,
    double MinLot,
    double MultipleLot);

public sealed record SimulationVariantDefinitionDto(
    int Id,
    int TemplateArticleId,
    string Size,
    string Color,
    double SizeCoefficient,
    double ColorCoefficient,
    string GeneratedArticleCode);

public sealed record CreateSimulationRequest(string Name, string? Description, string CreatedBy = "user");

public sealed record GenerateVariantsRequest(
    int SimulationId,
    int TemplateArticleId,
    IReadOnlyList<SizeCoefficientInput> Sizes,
    IReadOnlyList<ColorCoefficientInput> Colors);

public sealed record SizeCoefficientInput(string Size, double Coefficient);
public sealed record ColorCoefficientInput(string Color, double Coefficient);

public sealed record DuplicationSizeOptionDto(
    string Size,
    double Coefficient,
    bool IsSelected,
    int SortOrder);

public sealed record DuplicationColorOptionDto(
    string Color,
    double Coefficient,
    bool IsSelected,
    int SortOrder);

public sealed record DuplicationOptionsDto(
    IReadOnlyList<DuplicationSizeOptionDto> Sizes,
    IReadOnlyList<DuplicationColorOptionDto> Colors);

public sealed record SaveDuplicationOptionsRequest(
    int SimulationId,
    IReadOnlyList<DuplicationSizeOptionDto> Sizes,
    IReadOnlyList<DuplicationColorOptionDto> Colors);

public sealed record SeedTemplateArticleRequest(int SimulationId, string ArticleName);

public sealed record SeedTemplateArticleResult(int ArticleId, string ArticleCode, bool AlreadyExisted);

public sealed record CreateSimArticleRequest(
    int SimulationId,
    string Code,
    string Designation,
    string ArticleType,
    string ProcurementType,
    int LeadTimeDays,
    bool IsTemplate = false,
    string Unit = SimulationBomUnits.Un);

public sealed record UpsertSimBomLineRequest(
    int SimulationId,
    int ParentArticleId,
    int? BomLineId,
    int ComponentArticleId,
    double QuantityPer,
    double ScrapRate = 0,
    int OffsetDays = 0,
    bool ApplySizeCoefficient = false,
    bool ApplyColorSubstitution = false,
    string Unit = SimulationBomUnits.Un,
    string NomenclatureType = SimulationNomenclatureTypes.Base,
    int Alternative = 0);

public sealed record DeleteSimBomLineRequest(int SimulationId, int BomLineId);

public sealed record UpsertSalesOrderRequest(
    int SimulationId,
    string CustomerName,
    string CustomerOrderNumber,
    int ArticleId,
    double Quantity,
    DateOnly RequestedDate,
    int Priority = 50);

public sealed record UpdateSimCbnParameterRequest(
    int SimulationId,
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

public sealed record CbnRunRequestDto(
    int SimulationId,
    int SalesOrderId,
    int ArticleId,
    double Quantity,
    DateOnly RequestedDate);

public sealed record FlatBomLineDto(
    int Level,
    string? ParentArticleCode,
    string ComponentArticleCode,
    double QuantityPer,
    double CumulativeQuantity,
    DateOnly NeedDate,
    string Path);

public sealed record GrossRequirementDto(
    string ArticleCode,
    int Level,
    double GrossQuantity,
    DateOnly NeedDate,
    string SourceType,
    string? Path);

public sealed record NetRequirementDto(
    string ArticleCode,
    double GrossRequirement,
    double OnHandStock,
    double SafetyStock,
    double ReservedQuantity,
    double ScheduledReceiptProduction,
    double ScheduledReceiptPurchase,
    double NetRequirement,
    DateOnly NeedDate);

public sealed record PlannedOrderDto(
    string OrderType,
    string ArticleCode,
    double Quantity,
    DateOnly ReleaseDate,
    DateOnly ReceiptDate,
    string Source,
    string Justification);

public sealed record WorkOrderDto(
    string ArticleCode,
    double Quantity,
    DateOnly StartDate,
    DateOnly EndDate,
    string Status);

public sealed record PeggingNodeDto(
    string ArticleCode,
    double Quantity,
    int Level,
    DateOnly NeedDate,
    string SourceType,
    string Path,
    IReadOnlyList<PeggingNodeDto> Children);

public sealed record CbnAlertDto(
    string? ArticleCode,
    string AlertType,
    string Message,
    string Severity);

public sealed record SimCbnRunHistoryDto(
    int Id,
    int SimulationId,
    DateTime RunAt,
    string ArticleCode,
    string CustomerName,
    string CustomerOrderNumber,
    double Quantity,
    DateOnly RequestedDate,
    int PlannedOrderCount,
    int AlertCount,
    int NetLineCount);

public sealed record SimCbnTraceStepDto(
    string ArticleCode,
    int Level,
    double GrossRequirement,
    double OnHandStock,
    double SafetyStock,
    double ReservedQuantity,
    double ScheduledReceiptProduction,
    double ScheduledReceiptPurchase,
    double NetRequirement,
    DateOnly NeedDate,
    string? OrderType,
    double? PlannedQuantity,
    DateOnly? ReleaseDate,
    DateOnly? ReceiptDate,
    string? Justification,
    string? Path);

public sealed record CbnRunResultDto(
    int RunId,
    IReadOnlyList<FlatBomLineDto> FlatBomLines,
    IReadOnlyList<GrossRequirementDto> GrossRequirements,
    IReadOnlyList<NetRequirementDto> NetRequirements,
    IReadOnlyList<PlannedOrderDto> PlannedOrders,
    IReadOnlyList<WorkOrderDto> WorkOrders,
    IReadOnlyList<PeggingNodeDto> PeggingTree,
    IReadOnlyList<CbnAlertDto> Alerts,
    IReadOnlyList<SimCbnTraceStepDto> TraceSteps);

public sealed record GenerateVariantsResult(
    int SimulationId,
    IReadOnlyList<SimulationArticleDto> GeneratedArticles);
