using Axioplan.GammesNomenclatures.Application.Models;

namespace Axioplan.GammesNomenclatures.Application.Abstractions;

public interface ISimulationRepository
{
    Task<IReadOnlyList<FamilyListItem>> GetFamiliesAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProfileListItem>> GetProfilesAsync(
        string familyCode,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AttributeOptionListItem>> GetSizeOptionsAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AttributeOptionListItem>> GetColorOptionsAsync(CancellationToken cancellationToken = default);

    Task<SimulationResult> SimulateAsync(
        SimulationRequest request,
        CancellationToken cancellationToken = default);
}
