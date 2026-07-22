using Axioplan.GammesNomenclatures.Application.Models;

namespace Axioplan.GammesNomenclatures.Application.Abstractions;

public interface IImportRepository
{
    Task<IReadOnlyList<ImportFamilyItem>> GetFamiliesAsync(CancellationToken cancellationToken = default);

    Task<WorkbookImportAnalysis> AnalyzeOrderWorkbookAsync(
        byte[] fileContent,
        string fileName,
        CancellationToken cancellationToken = default);

    Task<WorkbookImportAnalysis> AnalyzeBomWorkbookAsync(
        byte[] fileContent,
        string fileName,
        CancellationToken cancellationToken = default);

    Task<ImportExecutionResult> ImportOrderAsync(
        byte[] fileContent,
        OrderImportRequest request,
        CancellationToken cancellationToken = default);

    Task<ImportExecutionResult> ImportBomAsync(
        byte[] fileContent,
        BomImportRequest request,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ImportMappingProfile>> GetMappingProfilesAsync(
        string importTarget,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BomExtractedPreviewRow>> PreviewBomImportAsync(
        byte[] fileContent,
        BomImportRequest request,
        CancellationToken cancellationToken = default);
}
