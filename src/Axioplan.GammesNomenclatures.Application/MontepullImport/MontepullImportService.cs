using Axioplan.GammesNomenclatures.Domain.MontepullImport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Axioplan.GammesNomenclatures.Application.MontepullImport;

public static class MontepullImportDependencyInjection
{
    public static IServiceCollection AddMontepullImportApplication(this IServiceCollection services)
    {
        services.AddScoped<MontepullImportService>();
        services.AddScoped<MontepullDatasetService>();
        return services;
    }
}

public sealed class MontepullDatasetOptions
{
    public const string SectionName = "Dataset";
    public string Active { get; set; } = "DEMO"; // DEMO | MONTEPULL_REAL
}

public sealed class MontepullDatasetService(
    IMontepullImportRepository repository,
    IOptions<MontepullDatasetOptions> options)
{
    public string ConfiguredActive => string.IsNullOrWhiteSpace(options.Value.Active) ? "DEMO" : options.Value.Active.Trim().ToUpperInvariant();

    public Task<string> GetActiveDatasetAsync(CancellationToken cancellationToken = default)
        => repository.GetActiveDatasetAsync(cancellationToken);

    public Task SetActiveDatasetAsync(string datasetCode, string? updatedBy = null, CancellationToken cancellationToken = default)
        => repository.SetActiveDatasetAsync(datasetCode, updatedBy, cancellationToken);
}

public sealed record MontepullAnomalyDto(
    string Severity,
    string Code,
    string Message,
    string? SheetName,
    int? SourceRowNo);

public sealed record MontepullFilePreviewDto(
    string FileName,
    MontepullFileKind Kind,
    string FileHash,
    IReadOnlyList<string> SheetNames,
    IReadOnlyList<IReadOnlyList<string>> HeaderPreview,
    int EstimatedRows);

public sealed record MontepullImportSummaryDto(
    long BatchId,
    Guid BatchUid,
    string Status,
    int LinesRead,
    int LinesValid,
    int LinesWarning,
    int LinesRejected,
    int CommandesDetected,
    int OfDetected,
    int OperationsDetected,
    int ArticlesDetected,
    int DuplicatesDetected,
    IReadOnlyList<MontepullAnomalyDto> Anomalies);

public sealed record MontepullBatchListItem(
    long Id,
    Guid BatchUid,
    string Status,
    DateTime ImportedAt,
    string? Origin,
    string DatasetCode,
    string? SummaryJson);

public interface IMontepullImportRepository
{
    Task EnsureSchemaAsync(CancellationToken cancellationToken = default);

    Task<MontepullFilePreviewDto> AnalyzeFileAsync(byte[] content, string fileName, CancellationToken cancellationToken = default);

    Task<MontepullImportSummaryDto> StageFilesAsync(
        IReadOnlyList<(byte[] Content, string FileName)> files,
        string? importedBy,
        string origin,
        CancellationToken cancellationToken = default);

    Task<MontepullImportSummaryDto> ValidateBatchAsync(long batchId, CancellationToken cancellationToken = default);

    Task<MontepullImportSummaryDto> PromoteBatchAsync(long batchId, CancellationToken cancellationToken = default);

    Task CancelBatchAsync(long batchId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MontepullBatchListItem>> ListBatchesAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MontepullAnomalyDto>> GetAnomaliesAsync(long batchId, CancellationToken cancellationToken = default);

    Task<string> GetAnomaliesReportCsvAsync(long batchId, CancellationToken cancellationToken = default);

    Task<string> GetActiveDatasetAsync(CancellationToken cancellationToken = default);

    Task SetActiveDatasetAsync(string datasetCode, string? updatedBy, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<(string OfCode, string? OrderCode, string? ArticleCode, double Qty, string? Status)>> ListImportedManufacturingOrdersAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Domain.Aps.Flux.ApsLaunchQuantity>> ResolveMontepullLaunchesAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default);

    Task EnsureMontepullCapacityResourcesAsync(CancellationToken cancellationToken = default);
}

public sealed class MontepullImportService(IMontepullImportRepository repository)
{
    public Task EnsureSchemaAsync(CancellationToken cancellationToken = default)
        => repository.EnsureSchemaAsync(cancellationToken);

    public Task<MontepullFilePreviewDto> AnalyzeFileAsync(byte[] content, string fileName, CancellationToken cancellationToken = default)
        => repository.AnalyzeFileAsync(content, fileName, cancellationToken);

    public Task<MontepullImportSummaryDto> StageFilesAsync(
        IReadOnlyList<(byte[] Content, string FileName)> files,
        string? importedBy = null,
        string origin = "UI",
        CancellationToken cancellationToken = default)
        => repository.StageFilesAsync(files, importedBy, origin, cancellationToken);

    public Task<MontepullImportSummaryDto> ValidateBatchAsync(long batchId, CancellationToken cancellationToken = default)
        => repository.ValidateBatchAsync(batchId, cancellationToken);

    public Task<MontepullImportSummaryDto> PromoteBatchAsync(long batchId, CancellationToken cancellationToken = default)
        => repository.PromoteBatchAsync(batchId, cancellationToken);

    public Task CancelBatchAsync(long batchId, CancellationToken cancellationToken = default)
        => repository.CancelBatchAsync(batchId, cancellationToken);

    public Task<IReadOnlyList<MontepullBatchListItem>> ListBatchesAsync(CancellationToken cancellationToken = default)
        => repository.ListBatchesAsync(cancellationToken);

    public Task<IReadOnlyList<MontepullAnomalyDto>> GetAnomaliesAsync(long batchId, CancellationToken cancellationToken = default)
        => repository.GetAnomaliesAsync(batchId, cancellationToken);

    public Task<string> GetAnomaliesReportCsvAsync(long batchId, CancellationToken cancellationToken = default)
        => repository.GetAnomaliesReportCsvAsync(batchId, cancellationToken);

    public Task<IReadOnlyList<(string OfCode, string? OrderCode, string? ArticleCode, double Qty, string? Status)>> ListImportedManufacturingOrdersAsync(
        CancellationToken cancellationToken = default)
        => repository.ListImportedManufacturingOrdersAsync(cancellationToken);
}
