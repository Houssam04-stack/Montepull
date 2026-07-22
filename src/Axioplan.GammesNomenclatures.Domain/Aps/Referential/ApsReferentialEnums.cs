namespace Axioplan.GammesNomenclatures.Domain.Aps.Referential;

public static class ApsResourceTypes
{
    public const string Machine = "MACHINE";
    public const string Labour = "MAIN_OEUVRE";
    public const string Lot = "LOT";
}

public static class ApsDecouplingPoints
{
    public const string Yarn = "FIL";
    public const string KnittedPart = "PIECE_TRICOTEE";
    public const string FinishedGood = "PF";
    public const string None = "NULL";
}

public static class ApsTraceabilityTypes
{
    public const string None = "AUCUNE";
    public const string Lot = "LOT";
    public const string Bath = "BAIN";
}

public static class ApsStockStatuses
{
    public const string Free = "LIBRE";
    public const string ReservedMts = "RESERVE_MTS";
    public const string ReservedOrder = "RESERVE_COMMANDE";
}

public static class ApsCircuitTypes
{
    public const string Internal = "INTERNE";
    public const string External = "EXTERNE";
}

public static class ApsBathConstraints
{
    public const string SameBath = "MEME_BAIN";
    public const string Free = "LIBRE";
}

public static class ApsPlaceLevels
{
    public const string Country = "PAYS";
    public const string Site = "SITE";
    public const string Plant = "USINE";
    public const string Zone = "ZONE";
    public const string Aisle = "ALLEE";
    public const string Bay = "TRAVEE";
    public const string Level = "NIVEAU";
    public const string Position = "POSITION";
}

public static class ApsStockZoneRoles
{
    public const string Upstream = "AMONT";
    public const string Wip = "EN_COURS";
    public const string Downstream = "AVAL";
    public const string Free = "LIBRE";
}
