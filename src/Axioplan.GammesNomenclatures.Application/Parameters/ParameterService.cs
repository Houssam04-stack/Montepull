using Axioplan.GammesNomenclatures.Application.Abstractions;
using Axioplan.GammesNomenclatures.Application.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Axioplan.GammesNomenclatures.Application.Parameters;

public static class ParameterDependencyInjection
{
    public static IServiceCollection AddParameterApplication(this IServiceCollection services)
    {
        services.AddScoped<ParameterService>();
        return services;
    }
}

public sealed class ParameterService(IParameterRepository repository)
{
    public Task<IReadOnlyList<ParameterFamilyItem>> GetFamiliesAsync(CancellationToken cancellationToken = default)
        => repository.GetFamiliesAsync(cancellationToken);

    public Task<IReadOnlyList<CoefficientItem>> GetConsumptionCoefficientsAsync(
        string familyCode,
        CancellationToken cancellationToken = default)
        => repository.GetConsumptionCoefficientsAsync(familyCode, cancellationToken);

    public Task<IReadOnlyList<CoefficientItem>> GetTimeCoefficientsAsync(
        string familyCode,
        CancellationToken cancellationToken = default)
        => repository.GetTimeCoefficientsAsync(familyCode, cancellationToken);

    public Task<IReadOnlyList<BomLossRateItem>> GetBomLossRatesAsync(
        string familyCode,
        CancellationToken cancellationToken = default)
        => repository.GetBomLossRatesAsync(familyCode, cancellationToken);

    public Task<IReadOnlyList<AttributeOptionAdminItem>> GetAttributeOptionsAsync(
        string attributeCode,
        CancellationToken cancellationToken = default)
        => repository.GetAttributeOptionsAsync(attributeCode, cancellationToken);

    public Task<IReadOnlyList<MvpParameterItem>> GetMvpParametersAsync(CancellationToken cancellationToken = default)
        => repository.GetMvpParametersAsync(cancellationToken);

    public Task CreateAttributeOptionAsync(
        CreateParameterAttributeOptionRequest request,
        CancellationToken cancellationToken = default)
        => repository.CreateAttributeOptionAsync(request, cancellationToken);

    public Task UpdateAttributeOptionStatusAsync(
        UpdateParameterAttributeOptionStatusRequest request,
        CancellationToken cancellationToken = default)
        => repository.UpdateAttributeOptionStatusAsync(request, cancellationToken);

    public Task UpdateConsumptionCoefficientAsync(UpdateCoefficientRequest request, CancellationToken cancellationToken = default)
        => repository.UpdateConsumptionCoefficientAsync(request, cancellationToken);

    public Task UpdateTimeCoefficientAsync(UpdateCoefficientRequest request, CancellationToken cancellationToken = default)
        => repository.UpdateTimeCoefficientAsync(request, cancellationToken);

    public Task UpdateBomLossRateAsync(UpdateLossRateRequest request, CancellationToken cancellationToken = default)
        => repository.UpdateBomLossRateAsync(request, cancellationToken);

    public Task UpdateMvpParameterAsync(UpdateMvpParameterRequest request, CancellationToken cancellationToken = default)
        => repository.UpdateMvpParameterAsync(request, cancellationToken);

    public Task<FormulaConfiguratorState> GetFormulaConfiguratorAsync(string familyCode, CancellationToken cancellationToken = default)
        => repository.GetFormulaConfiguratorAsync(familyCode, cancellationToken);

    public Task SaveRequirementFormulaAsync(SaveRequirementFormulaRequest request, CancellationToken cancellationToken = default)
        => repository.SaveRequirementFormulaAsync(request, cancellationToken);

    public Task<int> CreateCalculationArgumentAsync(CreateCalculationArgumentRequest request, CancellationToken cancellationToken = default)
        => repository.CreateCalculationArgumentAsync(request, cancellationToken);

    public Task UpdateCalculationArgumentAsync(UpdateCalculationArgumentRequest request, CancellationToken cancellationToken = default)
        => repository.UpdateCalculationArgumentAsync(request, cancellationToken);

    public Task CreateArgumentValueAsync(CreateArgumentValueRequest request, CancellationToken cancellationToken = default)
        => repository.CreateArgumentValueAsync(request, cancellationToken);

    public Task UpdateArgumentValueCoefficientAsync(UpdateArgumentValueCoefficientRequest request, CancellationToken cancellationToken = default)
        => repository.UpdateArgumentValueCoefficientAsync(request, cancellationToken);

    public Task UpdateArgumentValueStatusAsync(UpdateArgumentValueStatusRequest request, CancellationToken cancellationToken = default)
        => repository.UpdateArgumentValueStatusAsync(request, cancellationToken);

    public Task DeleteArgumentValueAsync(DeleteArgumentValueRequest request, CancellationToken cancellationToken = default)
        => repository.DeleteArgumentValueAsync(request, cancellationToken);

    public Task DeleteCalculationArgumentAsync(DeleteCalculationArgumentRequest request, CancellationToken cancellationToken = default)
        => repository.DeleteCalculationArgumentAsync(request, cancellationToken);
}
