namespace Axioplan.GammesNomenclatures.Application.Models;

public static class ImportTargets
{
    public const string Order = "ORDER";
    public const string Bom = "BOM";
}

public static class ImportFieldKeys
{
    public const string OrderCustomer = "order.customer";
    public const string OrderReference = "order.reference";
    public const string OrderDueDate = "order.due_date";
    public const string OrderGauge = "order.gauge";
    public const string OrderVersion = "order.version";
    public const string BomReference = "bom.reference";
    public const string BomDesignation = "bom.designation";
    public const string BomSupplier = "bom.supplier";
    public const string BomPlacement = "bom.placement";
    public const string BomUsage = "bom.usage";
    public const string BomNeed = "bom.need";
    public const string BomUnitPrice = "bom.unit_price";
}

public sealed record ImportFamilyItem(string Code, string Label);

public sealed record ImportFieldDefinition(
    string Key,
    string Label,
    bool Required,
    string? HelpText = null);

public sealed record ImportFieldMapping(
    string FieldKey,
    int? ColumnIndex,
    string? SourceHeader,
    bool IsAutoDetected,
    string? Notes = null);

public sealed record ImportPreviewRow(
    int RowNumber,
    IReadOnlyList<string> Cells);

public sealed record BomExtractedPreviewRow(
    int SourceRowNumber,
    string? Reference,
    string? Designation,
    string? Emploi,
    string? Besoin,
    string? Supplier,
    double Quantity,
    string Unit,
    string Behavior,
    string Status,
    IReadOnlyList<string> Warnings);

public sealed record ImportMappingProfile(
    int Id,
    string ImportTarget,
    string StructureFingerprint,
    string ProfileName,
    int HeaderRow,
    int DataStartRow,
    IReadOnlyList<ImportFieldMapping> Mappings);

public sealed record ImportSheetAnalysis(
    string SheetName,
    int RowCount,
    int ColumnCount,
    IReadOnlyList<ImportPreviewRow> PreviewRows,
    IReadOnlyList<int> SuggestedHeaderRows,
    IReadOnlyList<ImportFieldMapping> SuggestedMappings,
    int? SuggestedMetadataLabelRow = null,
    int? SuggestedMetadataValueRow = null,
    int? SuggestedQuantityRow = null,
    int? SuggestedDataStartRow = null,
    string? StructureFingerprint = null,
    ImportMappingProfile? MatchedProfile = null,
    IReadOnlyList<BomExtractedPreviewRow>? ExtractedPreviewRows = null);

public sealed record WorkbookImportAnalysis(
    string ImportTarget,
    string FileName,
    IReadOnlyList<ImportFieldDefinition> FieldDefinitions,
    IReadOnlyList<ImportSheetAnalysis> Sheets,
    IReadOnlyList<string> Warnings);

public sealed record OrderImportRequest(
    string FileName,
    string SheetName,
    string ProductFamilyCode,
    int MetadataLabelRow,
    int MetadataValueRow,
    int SizeHeaderRow,
    int QuantityRow,
    IReadOnlyList<ImportFieldMapping> Mappings);

public sealed record BomImportRequest(
    string FileName,
    string SheetName,
    string ProductFamilyCode,
    int HeaderRow,
    int DataStartRow,
    IReadOnlyList<ImportFieldMapping> Mappings,
    bool SaveMappingProfile = false,
    string? MappingProfileName = null);

public sealed record ImportExecutionResult(
    string ImportTarget,
    string FileName,
    string SheetName,
    string ProductFamilyCode,
    int ImportedRowCount,
    string Summary,
    IReadOnlyList<string> Warnings);
