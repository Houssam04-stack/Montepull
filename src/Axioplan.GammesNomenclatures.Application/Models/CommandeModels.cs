namespace Axioplan.GammesNomenclatures.Application.Models.Commandes;

public sealed record CommandeListItem(
    int Id,
    string SoCode,
    string? CustomerCode,
    string? CustomerLabel,
    DateTime CreatedAt,
    string? EnteredBy,
    string Status,
    int LineCount);

public sealed record CommandeLineDto(
    int Id,
    int LineNo,
    int ArticleId,
    string ArticleCode,
    string ArticleLabel,
    string? Model,
    string? Description,
    string? SupplierRef,
    string? ColorCode,
    string? ColorLabel,
    string? SizeLabel,
    double Quantity,
    string Unit,
    double? UnitPrice,
    double? LineAmount);

public sealed record CommandeDetailDto(
    int Id,
    string SoCode,
    string? CustomerCode,
    string? CustomerLabel,
    string? Label,
    string Status,
    DateTime CreatedAt,
    DateTime? ModifiedAt,
    DateTime? OrderDate,
    DateTime? AvailabilityDate,
    string? EnteredBy,
    string? ShippingAddress,
    string? BillingAddress,
    string? DeliveryMode,
    string? Notes,
    IReadOnlyList<CommandeLineDto> Lines);

public sealed record CommandeLineInput(
    int? ArticleId,
    string? ArticleCode,
    string? Model,
    string? Description,
    string? SupplierRef,
    string? ColorCode,
    string? ColorLabel,
    string? SizeLabel,
    double Quantity,
    string Unit,
    double? UnitPrice);

public sealed record CreateCommandeRequest(
    string? CustomerCode,
    string? EnteredBy,
    string? ShippingAddress,
    string? BillingAddress,
    string? DeliveryMode,
    DateTime? AvailabilityDate,
    string? Notes,
    IReadOnlyList<CommandeLineInput> Lines);

public sealed record UpdateCommandeRequest(
    int Id,
    string? CustomerCode,
    string? EnteredBy,
    string? ShippingAddress,
    string? BillingAddress,
    string? DeliveryMode,
    DateTime? AvailabilityDate,
    string? Notes,
    string Status,
    IReadOnlyList<CommandeLineInput> Lines);

public sealed record CommandeHistoryItem(
    int Id,
    int SalesOrderId,
    string SoCode,
    string EventType,
    DateTime CreatedAt,
    string? CustomerCode,
    string? EnteredBy);

public sealed record ArticlePickItem(
    int Id,
    string Code,
    string Label,
    string ArticleType);
