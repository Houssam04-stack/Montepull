namespace Axioplan.GammesNomenclatures.Application.Models;

public sealed record StockFilterOption(string Code, string Label);

public sealed record StockFilterOptionsDto(
    IReadOnlyList<StockFilterOption> Categories,
    IReadOnlyList<StockFilterOption> Families,
    IReadOnlyList<string> ArticleTypes,
    IReadOnlyList<StockFilterOption> BomBases);

public sealed record StockBalanceRow(
    int Id,
    int ArticleId,
    string ArticleCode,
    string ArticleLabel,
    string ArticleType,
    string CategoryLabel,
    string FamilyLabel,
    double QuantityAvailable,
    string Unit,
    DateTime UpdatedAt);

public sealed record StockLotLineRow(
    int Id,
    int ArticleId,
    string ArticleCode,
    string ArticleLabel,
    string ArticleType,
    string CategoryLabel,
    string FamilyLabel,
    string? LocationCode,
    string? Warehouse,
    string? LotCode,
    string? SupplierLot,
    double QuantityAllocated,
    double QuantityInProgress,
    double QuantityInStock,
    double QuantityAvailable,
    string? Status,
    string? LocationType,
    string Unit,
    DateOnly? ReceivedDate,
    DateOnly? LastReceiptDate,
    DateOnly? LastIssueDate,
    DateTime UpdatedAt);

public sealed record StockNomenclatureLineRow(
    int Id,
    string BomCode,
    int BomVersion,
    string? ProductFamilyCode,
    string? ParentArticleCode,
    string? ParentArticleLabel,
    int LineNo,
    int ComponentArticleId,
    string ComponentArticleCode,
    string ComponentArticleLabel,
    string ComponentArticleType,
    string CategoryLabel,
    string FamilyLabel,
    double QuantityBase,
    string Unit,
    double LossRate,
    string Behavior);
