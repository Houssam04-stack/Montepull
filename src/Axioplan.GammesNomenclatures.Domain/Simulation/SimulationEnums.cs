namespace Axioplan.GammesNomenclatures.Domain.Simulation;

/// <summary>
/// Enums du module Simulation CBN/MRP (stockes en NVARCHAR cote SQL Server).
/// </summary>
public static class SimulationArticleTypes
{
    public const string FinishedGood = "FinishedGood";
    public const string SemiFinished = "SemiFinished";
    public const string RawMaterial = "RawMaterial";
    public const string Purchased = "Purchased";
    public const string Manufactured = "Manufactured";
}

public static class SimulationProcurementTypes
{
    public const string Manufactured = "Manufactured";
    public const string Purchased = "Purchased";
}

public static class SimulationLotRules
{
    public const string LotForLot = "LotForLot";
    public const string FixedQuantity = "FixedQuantity";
    public const string MinimumQuantity = "MinimumQuantity";
    public const string MultipleQuantity = "MultipleQuantity";
}

public static class SimulationOrderTypes
{
    public const string Of = "OF";
    public const string Oa = "OA";
}

public static class SimulationAlertTypes
{
    public const string PastNeed = "PAST_NEED";
    public const string PastRelease = "PAST_RELEASE";
    public const string BomCycle = "BOM_CYCLE";
    public const string MissingCbnParameter = "MISSING_CBN_PARAMETER";
    public const string MissingLeadTime = "MISSING_LEAD_TIME";
    public const string MissingComponent = "MISSING_COMPONENT";
    public const string InvalidBom = "INVALID_BOM";
}
