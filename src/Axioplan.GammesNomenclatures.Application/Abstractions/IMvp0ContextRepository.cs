using Axioplan.GammesNomenclatures.Application.Models;
using Axioplan.GammesNomenclatures.Domain.Mvp0;
namespace Axioplan.GammesNomenclatures.Application.Abstractions;
public interface IMvp0ContextRepository
{
 Task<Mvp0BusinessContext?> GetRunContextAsync(int cbn,int? pegging,CancellationToken ct=default);
 Task<Mvp0BusinessContext?> GetCampaignContextAsync(Guid id,CancellationToken ct=default);
 Task<Guid> CreateOrGetCampaignAsync(Mvp0Campaign campaign,int cbn,int pegging,CancellationToken ct=default);
 Task<Mvp0ContextSnapshot> LoadSnapshotAsync(int cbn,CancellationToken ct=default);
}
