namespace Axioplan.GammesNomenclatures.Application.Models;

public sealed record AttributeOptionListItem(string Code, string Label);

public sealed record SalesOrderListItem(int Id, string Code, string? Label, string Status, int LineCount);

public sealed record SalesOrderLineItem(
    int Id,
    int LineNo,
    string ArticleCode,
    string? SizeCode,
    string? ColorCode,
    double Quantity,
    string Unit,
    string? ExternalRef);

/// <summary>Commande métier importée (Montepull / sales_orders) pour l'onglet Commandes.</summary>
public sealed record ImportedSalesOrderDetail(
    int Id,
    string Code,
    string? Label,
    string? CustomerCode,
    string Status,
    DateTime? OrderDate,
    string? DataSource,
    int? ImportBatchId,
    int LineCount,
    double TotalQuantity,
    string ArticlePreview,
    IReadOnlyList<ImportedSalesOrderLineDetail> Lines);

/// <summary>Ligne au format commandes.xlsx (champs métier + optionnels staging).</summary>
public sealed record ImportedSalesOrderLineDetail(
    int Id,
    int LineNo,
    string ArticleCode,
    string? Designation,
    string? SizeCode,
    string? ColorCode,
    double Quantity,
    double QtyLaunched,
    double QtyProduced,
    int? LastOperation,
    double? SommeOperations,
    double? SommeOp70Commande,
    double? SommeOp70Article,
    string Unit,
    string? ExternalRef,
    double? RemainingToLaunch,
    double? RemainingToProduce,
    DateTime? DeliveryDate);

/// <summary>Création / mise à jour d'une ligne de commande (mêmes champs que commandes.xlsx).</summary>
public sealed record UpsertImportedSalesOrderLineRequest(
    string OrderCode,
    string? CustomerCode,
    string ArticleCode,
    string? Designation,
    double QtyOrdered,
    double QtyLaunched = 0,
    double QtyProduced = 0,
    int? LastOperation = null,
    double? SommeOperations = null,
    double? SommeOp70Commande = null,
    double? SommeOp70Article = null,
    string? DataSource = null);

public sealed record CbnRunRequest(int SalesOrderId, string ProductFamilyCode);

public sealed record CbnFlattenedBomRow(
    int LineNo,
    string ComponentCode,
    string ComponentLabel,
    string ComponentType,
    string CalculationMode,
    int BomLevel,
    double QuantityPerUnit,
    string Unit,
    double LossRate,
    string Behavior,
    string SourcePath);

public sealed record CbnRequirementRow(
    string VariantLabel,
    string ComponentCode,
    string ComponentLabel,
    string ComponentType,
    string CalculationMode,
    double QuantityNet,
    double QuantityGross,
    string Unit,
    string SourcePath);

public sealed record CbnComponentTotal(
    string ComponentCode,
    string ComponentLabel,
    string ComponentType,
    string CalculationMode,
    double TotalNet,
    double TotalGross,
    string Unit);

public sealed record CbnTraceRow(
    string VariantLabel,
    string ComponentCode,
    string CalculationMode,
    string RuleCode,
    double BaseValue,
    double OrderQuantity,
    double? SizeCoefficient,
    double? ColorCoefficient,
    double LossRate,
    double QuantityNet,
    double QuantityGross,
    string Unit,
    string Formula);

public sealed record CbnRunResult
{
    public int RunId { get; init; }
    public string SalesOrderCode { get; init; } = string.Empty;
    public string ProductFamilyCode { get; init; } = string.Empty;
    public string Status { get; init; } = "COMPLETED";
    public IReadOnlyList<CbnFlattenedBomRow> FlattenedBom { get; init; } = [];
    public IReadOnlyList<CbnRequirementRow> Requirements { get; init; } = [];
    public IReadOnlyList<CbnComponentTotal> ComponentTotals { get; init; } = [];
    public IReadOnlyList<CbnTraceRow> Traces { get; init; } = [];
}
