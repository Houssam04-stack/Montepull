namespace Axioplan.GammesNomenclatures.Domain.Aps.Expectations;

/// <summary>
/// Types d'attendus (M8). Vocabulaire versionne — immuable une fois emis.
/// </summary>
public static class ApsExpectationTypes
{
    public const string SchemaVersion = "1.0";

    public const string YarnPurchaseDeadline = "DateLimiteAchatFil";
    public const string YarnOrderToEmit = "CommandeFilAEmettre";
    public const string YarnReceiptExpected = "ReceptionFilAttendue";
    public const string ClientEngagementDeadline = "DateLimiteEngagementClient";
    public const string ShipmentExpected = "ExpeditionAttendue";
    public const string ExternalHandoverExpected = "RemiseExterneAttendue";
    public const string ExternalReturnExpected = "RetourExterneAttendu";
    public const string OpenContract = "ContratOuvert";
    public const string LoadExpected = "ChargeAttendue";
    public const string BottleneckBufferFeedExpected = "AlimentationBufferGoulotAttendue";
}

public static class ApsExpectationGrains
{
    public const string Month = "MOIS";
    public const string Week = "SEMAINE";
    public const string Day = "JOUR";
    public const string DayPost = "JOUR_POSTE";
    public const string Sequence = "SEQUENCE";
}

public static class ApsExpectationEmitters
{
    public const string M1 = "M1";
    public const string M2 = "M2";
    public const string M3 = "M3";
    public const string M4 = "M4";
    public const string M5 = "M5";
    public const string M6 = "M6";
}

/// <summary>
/// Invariant I-grain : valide le grain autorise selon le module emetteur.
/// </summary>
public static class ApsExpectationGrainRules
{
    public static bool IsAllowed(string emittedBy, string grain)
    {
        return emittedBy switch
        {
            ApsExpectationEmitters.M1 or ApsExpectationEmitters.M2
                => grain is ApsExpectationGrains.Month,
            ApsExpectationEmitters.M4 or ApsExpectationEmitters.M5
                => grain is ApsExpectationGrains.Week or ApsExpectationGrains.Day or ApsExpectationGrains.DayPost,
            ApsExpectationEmitters.M6
                => grain is ApsExpectationGrains.Sequence or ApsExpectationGrains.Day,
            ApsExpectationEmitters.M3
                => grain is ApsExpectationGrains.Day or ApsExpectationGrains.Week or ApsExpectationGrains.Month,
            _ => false
        };
    }
}

public sealed record ApsExpectation(
    long Id,
    string ExpectedId,
    string ExpectedType,
    string EmittedBy,
    string Grain,
    string? PlanRef,
    DateTime EarliestAtUtc,
    DateTime LatestAtUtc,
    double? Hre,
    string? CausationId,
    string AggregateId,
    string PayloadJson,
    DateTime EmittedAtUtc,
    string SchemaVersion);

public sealed record EmitApsExpectationCommand(
    string ExpectedId,
    string ExpectedType,
    string EmittedBy,
    string Grain,
    DateTime EarliestAtUtc,
    DateTime LatestAtUtc,
    string AggregateId,
    string PayloadJson,
    string? PlanRef = null,
    double? Hre = null,
    string? CausationId = null,
    string SchemaVersion = ApsExpectationTypes.SchemaVersion);
