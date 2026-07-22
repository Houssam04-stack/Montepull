namespace Axioplan.GammesNomenclatures.Application.Models;

public sealed record ParameterFamilyItem(string Code, string Label);

public sealed record CoefficientItem(
    int Id,
    string AttributeCode,
    string AttributeLabel,
    string OptionCode,
    string OptionLabel,
    double Coefficient,
    string Status,
    string SourceStatus);

public sealed record BomLossRateItem(
    int LineId,
    int LineNo,
    string BomCode,
    string ComponentCode,
    string ComponentLabel,
    double QuantityBase,
    string Unit,
    string Behavior,
    double LossRate);

public sealed record MvpParameterItem(
    int Id,
    string GroupCode,
    string GroupLabel,
    string Code,
    string Label,
    string ValueType,
    string Scope,
    string? Description,
    string Value);

public sealed record AttributeOptionAdminItem(
    int Id,
    string AttributeCode,
    string AttributeLabel,
    string TechnicalCode,
    string DisplayValue,
    string NormalizedValue,
    string Status);

public sealed record UpdateCoefficientRequest(int Id, double Coefficient);

public sealed record UpdateLossRateRequest(int LineId, double LossRate);

public sealed record UpdateMvpParameterRequest(string ParameterCode, string Value, string? ScopeKey = null);

public sealed record CreateParameterAttributeOptionRequest(string AttributeCode, string Value);

public sealed record UpdateParameterAttributeOptionStatusRequest(int OptionId, string Status);

public sealed record CalculationArgumentValueItem(
    int OptionId,
    int CoefficientId,
    string DisplayValue,
    string TechnicalCode,
    double Coefficient,
    string Status);

public sealed record CalculationArgumentItem(
    int Id,
    string Code,
    string Label,
    bool IsActive,
    IReadOnlyList<CalculationArgumentValueItem> Values);

public sealed record RequirementFormulaItem(
    int? Id,
    string FamilyCode,
    string Expression,
    string DisplayExpression,
    bool ApplyOrderQuantity,
    string Status);

public sealed record FormulaConfiguratorState(
    string FamilyCode,
    RequirementFormulaItem Formula,
    IReadOnlyList<CalculationArgumentItem> Arguments);

public sealed record SaveRequirementFormulaRequest(
    string FamilyCode,
    IReadOnlyList<string> Tokens,
    bool ApplyOrderQuantity);

public sealed record CreateCalculationArgumentRequest(string Code, string Label);

public sealed record UpdateCalculationArgumentRequest(int ArgumentId, string Label, bool IsActive);

public sealed record CreateArgumentValueRequest(string AttributeCode, string Value);

public sealed record UpdateArgumentValueCoefficientRequest(int CoefficientId, double Coefficient);

public sealed record UpdateArgumentValueStatusRequest(int OptionId, string Status);

public sealed record DeleteArgumentValueRequest(int OptionId);

public sealed record DeleteCalculationArgumentRequest(int ArgumentId, string FamilyCode);
