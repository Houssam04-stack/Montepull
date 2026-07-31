using Axioplan.GammesNomenclatures.Application.Mvp0;
using Axioplan.GammesNomenclatures.Application.Aps;
using Axioplan.GammesNomenclatures.Application.Cbn;
using Axioplan.GammesNomenclatures.Application.Commandes;
using Axioplan.GammesNomenclatures.Application.Imports;
using Axioplan.GammesNomenclatures.Application.MontepullImport;
using Axioplan.GammesNomenclatures.Application.Pegging;
using Axioplan.GammesNomenclatures.Application.Abstractions;
using Axioplan.GammesNomenclatures.Application.Models;
using Axioplan.GammesNomenclatures.Application.Parameters;
using Axioplan.GammesNomenclatures.Application.SimulationCbn;
using Axioplan.GammesNomenclatures.Application.Simulation;
using Axioplan.GammesNomenclatures.Application.Stock;
using Microsoft.Extensions.DependencyInjection;

namespace Axioplan.GammesNomenclatures.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<SimulationService>();
        services.AddArticleApplication();
        services.AddCommandeApplication();
        services.AddParameterApplication();
        services.AddCbnApplication();
        services.AddPeggingApplication();
        services.AddStockApplication();
        services.AddImportApplication();
        services.AddSimulationCbnApplication();
        services.AddGammesSimulationApplication();
        services.AddApsApplication();
        services.AddApsCapacityCbnApplication();
        services.AddApsFluxApplication();
        services.AddApsCtpApplication();
        services.AddApsPhase9Application();
        services.AddApsPlanningApplication();
        services.AddApsScheduleDecisionApplication();
        services.AddMvp0Application();
        services.AddMontepullImportApplication();
        services.AddPlanningDatasetProvider();
        return services;
    }
}

public sealed class SimulationService(ISimulationRepository repository)
{
    public Task<IReadOnlyList<FamilyListItem>> GetFamiliesAsync(CancellationToken cancellationToken = default)
        => repository.GetFamiliesAsync(cancellationToken);

    public Task<IReadOnlyList<ProfileListItem>> GetProfilesAsync(
        string familyCode,
        CancellationToken cancellationToken = default)
        => repository.GetProfilesAsync(familyCode, cancellationToken);

    public Task<IReadOnlyList<AttributeOptionListItem>> GetSizeOptionsAsync(CancellationToken cancellationToken = default)
        => repository.GetSizeOptionsAsync(cancellationToken);

    public Task<IReadOnlyList<AttributeOptionListItem>> GetColorOptionsAsync(CancellationToken cancellationToken = default)
        => repository.GetColorOptionsAsync(cancellationToken);

    public Task<SimulationResult> SimulateAsync(
        SimulationRequest request,
        CancellationToken cancellationToken = default)
        => repository.SimulateAsync(request, cancellationToken);
}
