namespace Axioplan.GammesNomenclatures.Application.Models;

public sealed record Mvp0ApsBridgeResult(
    string ProductFamilyCode,
    int ArticlesUpserted,
    int WorkcentersUpserted,
    int BomLinesCreated,
    int RoutingOperationsCreated,
    int BomOpLinksApplied,
    int BomBaseVersion,
    int RoutingBaseVersion);
