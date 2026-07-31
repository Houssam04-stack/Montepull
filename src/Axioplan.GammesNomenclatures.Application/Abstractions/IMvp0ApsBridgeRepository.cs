using Axioplan.GammesNomenclatures.Application.Models;
using Axioplan.GammesNomenclatures.Domain.Mvp0;

namespace Axioplan.GammesNomenclatures.Application.Abstractions;

public interface IMvp0ApsBridgeRepository
{
    Task<Mvp0ApsBridgeResult> PromoteWorkingSetAsync(
        Mvp0CampaignDto campaign,
        IReadOnlyList<Mvp0ArticleRow> articles,
        IReadOnlyList<Mvp0BomRow> boms,
        IReadOnlyList<Mvp0RoutingOpRow> ops,
        IReadOnlySet<string> centers,
        IReadOnlySet<string> bomOpLinks,
        CancellationToken cancellationToken = default);
}
