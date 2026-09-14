using Axioplan.GammesNomenclatures.Domain.Mvp0;
namespace Axioplan.GammesNomenclatures.Application.Models;
public sealed record Mvp0BusinessContext(int CbnRunId,int PeggingRunId,int PeggingVersion,string OrderCode,string FamilyCode,IReadOnlyList<string> ArticleCodes,string? SourceDataset,long? ImportedBatchId);
public sealed record Mvp0ContextSnapshot(IReadOnlyList<Mvp0ArticleRow> Articles,IReadOnlyList<Mvp0BomRow> Boms);
