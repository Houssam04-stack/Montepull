using Axioplan.GammesNomenclatures.Application.Models;

namespace Axioplan.GammesNomenclatures.Application.Abstractions;

public interface IParameterRepository
{
    Task<IReadOnlyList<ParameterFamilyItem>> GetFamiliesAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CoefficientItem>> GetConsumptionCoefficientsAsync(
        string familyCode,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CoefficientItem>> GetTimeCoefficientsAsync(
        string familyCode,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BomLossRateItem>> GetBomLossRatesAsync(
        string familyCode,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AttributeOptionAdminItem>> GetAttributeOptionsAsync(
        string attributeCode,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MvpParameterItem>> GetMvpParametersAsync(CancellationToken cancellationToken = default);

    Task CreateAttributeOptionAsync(CreateParameterAttributeOptionRequest request, CancellationToken cancellationToken = default);

    Task UpdateAttributeOptionStatusAsync(UpdateParameterAttributeOptionStatusRequest request, CancellationToken cancellationToken = default);

    Task UpdateConsumptionCoefficientAsync(UpdateCoefficientRequest request, CancellationToken cancellationToken = default);

    Task UpdateTimeCoefficientAsync(UpdateCoefficientRequest request, CancellationToken cancellationToken = default);

    Task UpdateBomLossRateAsync(UpdateLossRateRequest request, CancellationToken cancellationToken = default);

    Task UpdateMvpParameterAsync(UpdateMvpParameterRequest request, CancellationToken cancellationToken = default);

    Task<FormulaConfiguratorState> GetFormulaConfiguratorAsync(
        string familyCode,
        CancellationToken cancellationToken = default);

    Task SaveRequirementFormulaAsync(SaveRequirementFormulaRequest request, CancellationToken cancellationToken = default);

    Task<int> CreateCalculationArgumentAsync(CreateCalculationArgumentRequest request, CancellationToken cancellationToken = default);

    Task UpdateCalculationArgumentAsync(UpdateCalculationArgumentRequest request, CancellationToken cancellationToken = default);

    Task CreateArgumentValueAsync(CreateArgumentValueRequest request, CancellationToken cancellationToken = default);

    Task UpdateArgumentValueCoefficientAsync(UpdateArgumentValueCoefficientRequest request, CancellationToken cancellationToken = default);

    Task UpdateArgumentValueStatusAsync(UpdateArgumentValueStatusRequest request, CancellationToken cancellationToken = default);

    Task DeleteArgumentValueAsync(DeleteArgumentValueRequest request, CancellationToken cancellationToken = default);

    Task DeleteCalculationArgumentAsync(DeleteCalculationArgumentRequest request, CancellationToken cancellationToken = default);

    Task<RequirementFormulaItem?> GetRequirementFormulaAsync(
        string familyCode,
        CancellationToken cancellationToken = default);
}
