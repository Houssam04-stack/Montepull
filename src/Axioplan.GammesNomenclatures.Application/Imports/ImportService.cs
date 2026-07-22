using Axioplan.GammesNomenclatures.Application.Abstractions;
using Axioplan.GammesNomenclatures.Application.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Axioplan.GammesNomenclatures.Application.Imports;

public static class ImportDependencyInjection
{
    public static IServiceCollection AddImportApplication(this IServiceCollection services)
    {
        services.AddScoped<ImportService>();
        return services;
    }
}

public sealed class ImportService(IImportRepository repository)
{
    public Task<IReadOnlyList<ImportFamilyItem>> GetFamiliesAsync(CancellationToken cancellationToken = default)
        => repository.GetFamiliesAsync(cancellationToken);

    public Task<WorkbookImportAnalysis> AnalyzeOrderWorkbookAsync(
        byte[] fileContent,
        string fileName,
        CancellationToken cancellationToken = default)
        => repository.AnalyzeOrderWorkbookAsync(fileContent, fileName, cancellationToken);

    public Task<WorkbookImportAnalysis> AnalyzeBomWorkbookAsync(
        byte[] fileContent,
        string fileName,
        CancellationToken cancellationToken = default)
        => repository.AnalyzeBomWorkbookAsync(fileContent, fileName, cancellationToken);

    public Task<ImportExecutionResult> ImportOrderAsync(
        byte[] fileContent,
        OrderImportRequest request,
        CancellationToken cancellationToken = default)
        => repository.ImportOrderAsync(fileContent, request, cancellationToken);

    public Task<ImportExecutionResult> ImportBomAsync(
        byte[] fileContent,
        BomImportRequest request,
        CancellationToken cancellationToken = default)
        => repository.ImportBomAsync(fileContent, request, cancellationToken);

    public Task<IReadOnlyList<ImportMappingProfile>> GetMappingProfilesAsync(
        string importTarget,
        CancellationToken cancellationToken = default)
        => repository.GetMappingProfilesAsync(importTarget, cancellationToken);

    public Task<IReadOnlyList<BomExtractedPreviewRow>> PreviewBomImportAsync(
        byte[] fileContent,
        BomImportRequest request,
        CancellationToken cancellationToken = default)
        => repository.PreviewBomImportAsync(fileContent, request, cancellationToken);
}
