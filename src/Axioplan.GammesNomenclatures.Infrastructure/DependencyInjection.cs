using Axioplan.GammesNomenclatures.Application.Abstractions;
using Axioplan.GammesNomenclatures.Application.MontepullImport;
using Axioplan.GammesNomenclatures.Application.Mvp0;
using Axioplan.GammesNomenclatures.Infrastructure.Aps;
using Axioplan.GammesNomenclatures.Infrastructure.Data;
using Axioplan.GammesNomenclatures.Infrastructure.Logging;
using Axioplan.GammesNomenclatures.Infrastructure.Mvp0;
using Axioplan.GammesNomenclatures.Infrastructure.Repositories;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Axioplan.GammesNomenclatures.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<DatabaseOptions>(configuration.GetSection(DatabaseOptions.SectionName));
        services.Configure<MontepullDatasetOptions>(configuration.GetSection(MontepullDatasetOptions.SectionName));
        services.AddScoped<ISimulationRepository, SqlServerSimulationRepository>();
        services.AddScoped<IArticleRepository, SqlServerArticleRepository>();
        services.AddScoped<ICommandeRepository, SqlServerCommandeRepository>();
        services.AddScoped<IParameterRepository, SqlServerParameterRepository>();
        services.AddScoped<ICbnRepository, SqlServerCbnRepository>();
        services.AddScoped<IPeggingRepository, SqlServerPeggingRepository>();
        services.AddScoped<IConsultationRepository, SqlServerConsultationRepository>();
        services.AddScoped<IStockRepository, SqlServerStockRepository>();
        services.AddScoped<IImportRepository, SqlServerImportRepository>();
        services.AddScoped<ISimulationCbnRepository, SqlServerSimulationCbnRepository>();
        services.AddSingleton<ApsSchemaBootstrap>();
        services.AddSingleton<Mvp0SchemaBootstrap>();
        services.AddScoped<IMvp0Repository, SqlServerMvp0Repository>();
        services.AddScoped<IMvp0ApsBridgeRepository, SqlServerMvp0ApsBridgeRepository>();
        services.AddScoped<IApsJournalRepository, SqlServerApsJournalRepository>();
        services.AddScoped<IApsExpectationRepository, SqlServerApsExpectationRepository>();
        services.AddScoped<IApsReferentialRepository, SqlServerApsReferentialRepository>();
        services.AddScoped<IApsCompilerRepository, SqlServerApsCompilerRepository>();
        services.AddScoped<IApsCapacityRepository, SqlServerApsCapacityRepository>();
        services.AddScoped<IApsSegmentCbnRepository, SqlServerApsSegmentCbnRepository>();
        services.AddScoped<IApsFluxRepository, SqlServerApsFluxRepository>();
        services.AddScoped<IApsCtpRepository, SqlServerApsCtpRepository>();
        services.AddScoped<IApsPhase9Repository, SqlServerApsPhase9Repository>();
        services.AddScoped<IMontepullImportRepository, SqlServerMontepullImportRepository>();
        services.AddSingleton<SqlApplicationLogger>();
        return services;
    }
}
