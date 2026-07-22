namespace Axioplan.GammesNomenclatures.Domain.Aps.Journal;

/// <summary>
/// Vocabulaire versionne du journal des faits (M7). Append-only — ne jamais reutiliser un type obsolete.
/// </summary>
public static class ApsFactEventTypes
{
    public const string SchemaVersion = "1.0";

    // EXECUTION
    public const string OperationStarted = "OperationDemarree";
    public const string OperationFinished = "OperationTerminee";
    public const string ResourceStop = "ArretRessource";
    public const string SetupDone = "SetupRealise";
    public const string ConsumptionDeclared = "ConsommationDeclaree";
    public const string YarnReceipt = "ReceptionFil";
    public const string StockMove = "MouvementStock";
    public const string ExternalHandover = "RemiseExterne";
    public const string ExternalReturn = "RetourExterne";
    public const string ReceiptControl = "ControleReception";

    // PLANIFICATION
    public const string PlanPublished = "PlanPublie";
    public const string ContractEngaged = "ContratEngage";
    public const string ContractRevised = "ContratRevise";
    public const string PromiseMade = "PromesseFaite";
    public const string PromiseRescheduled = "PromesseReplanifiee";
    public const string PromiseRejected = "PromesseRefusee";
    public const string CapacityReserved = "CapaciteReservee";
    public const string CapacityReleased = "CapaciteLiberee";
    public const string MaterialReserved = "MatiereReservee";
    public const string MaterialReleased = "MatiereLiberee";
    public const string RegimeArbitration = "ArbitrageRegime";
    public const string BottleneckMoved = "GoulotDeplace";
    public const string EnvelopeViolated = "EnveloppeViolee";
    public const string YarnCatalogueRevised = "CatalogueFilRevise";
    public const string EngagementContracted = "EngagementContracte";

    // APPRENTISSAGE
    public const string DeviationObserved = "EcartConstate";
    public const string CoefficientRecalibrated = "CoefficientRecalibre";
    public const string CompiledInvalidated = "CompileInvalideRefuse";
    public const string CompileRecompiled = "CompileRecompile";
    public const string NightlyCycleStarted = "CycleNocturneDemarre";
    public const string NightlyCycleFinished = "CycleNocturneTermine";
    public const string RecipeEvaluated = "RecetteEvaluee";

    // FLUX / BUFFER (phase 7)
    public const string BufferMeasured = "BufferMesure";
    public const string RopeRecommendationPublished = "RecommandationRopePubliee";
    public const string StockZoneSaturated = "ZoneStockSaturee";

    // COMPENSATION
    public const string QuantityCorrected = "QuantiteCorrigee";
}

public sealed record ApsJournalEvent(
    long EventId,
    string EventType,
    DateTime OccurredAtUtc,
    DateTime RecordedAtUtc,
    string AggregateId,
    string PayloadJson,
    string Actor,
    string SchemaVersion,
    string? CausationId,
    string? CorrelationId);

public sealed record AppendApsJournalFactCommand(
    string EventType,
    DateTime OccurredAtUtc,
    string AggregateId,
    string PayloadJson,
    string Actor,
    string? CausationId = null,
    string? CorrelationId = null,
    string SchemaVersion = ApsFactEventTypes.SchemaVersion);
