using Axioplan.GammesNomenclatures.Application.Abstractions;
using Axioplan.GammesNomenclatures.Application.Models;
using Axioplan.GammesNomenclatures.Application.SimulationCbn;
using Axioplan.GammesNomenclatures.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace Axioplan.GammesNomenclatures.Application.Simulation;

public static class GammesSimulationDependencyInjection
{
    public static IServiceCollection AddGammesSimulationApplication(this IServiceCollection services)
    {
        services.AddScoped<GammesSimulationService>();
        return services;
    }
}

public sealed class GammesSimulationService(ISimulationCbnRepository repository)
{
    public async Task<GammesSimulationContext> EnsureContextAsync(CancellationToken cancellationToken = default)
    {
        await repository.EnsureSchemaAsync(cancellationToken);

        var simulations = await repository.ListSimulationsAsync(cancellationToken);
        var simulationId = simulations.Count == 0
            ? (await repository.CreateSimulationAsync(
                new CreateSimulationRequest("Demo Pantalon", "Simulation gammes & nomenclatures"),
                cancellationToken)).Id
            : simulations[0].Id;

        await repository.SeedPantalonDemoAsync(simulationId, cancellationToken);
        await repository.EnsureDefaultDuplicationOptionsAsync(simulationId, cancellationToken);
        var articles = await repository.GetArticlesAsync(simulationId, cancellationToken);
        var duplicationOptions = await repository.GetDuplicationOptionsAsync(simulationId, cancellationToken);

        return new GammesSimulationContext(simulationId, articles, duplicationOptions);
    }

    public Task SaveDuplicationOptionsAsync(
        SaveDuplicationOptionsRequest request,
        CancellationToken cancellationToken = default)
        => repository.SaveDuplicationOptionsAsync(request, cancellationToken);

    public async Task<SimulationResult> SimulateAsync(
        GammesSimulationRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Sizes.Count == 0 || request.Colors.Count == 0)
        {
            throw new InvalidOperationException("Selectionnez au moins une taille et une couleur.");
        }

        var articles = await repository.GetArticlesAsync(request.SimulationId, cancellationToken);
        var template = articles.FirstOrDefault(a => a.Id == request.TemplateArticleId)
            ?? throw new InvalidOperationException("Article introuvable dans la simulation MRP.");

        var bomDtos = await repository.GetBomLinesAsync(request.SimulationId, cancellationToken);
        var templateBom = bomDtos
            .Where(line => line.ParentArticleId == template.Id
                && string.Equals(line.NomenclatureType, SimulationNomenclatureTypes.Base, StringComparison.Ordinal))
            .ToList();

        if (templateBom.Count == 0 && !template.IsTemplate)
        {
            throw new InvalidOperationException(
                $"L'article {template.Code} n'a pas de nomenclature. Choisissez un article template (ex. PANTALON_BASE).");
        }

        if (templateBom.Count == 0)
        {
            throw new InvalidOperationException(
                "Nomenclature template introuvable. Lancez Seed PANTALON dans Simulation MRP.");
        }

        var mSize = request.Sizes.FirstOrDefault(s =>
            string.Equals(s.Size, "M", StringComparison.OrdinalIgnoreCase))
            ?? request.Sizes.OrderBy(s => s.Coefficient).First();

        var baseBom = BuildBaseBom(templateBom, articles, mSize.Coefficient);
        var componentQty = baseBom
            .GroupBy(row => row.ComponentCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.First().QuantityNet,
                StringComparer.OrdinalIgnoreCase);

        var routingOps = PantalonGammeTemplate.Build(componentQty);
        var variantCount = request.Sizes.Count * request.Colors.Count;
        var totalRoutingTime = routingOps.Sum(op => op.TimeMinutes);

        var productInfo = new SimProductInfo(
            template.Code,
            template.Designation,
            $"Nomenclature {template.Code} (taille M)",
            "Gamme pantalon (fichier prod — temps fictifs)");

        return new SimulationResult
        {
            Product = productInfo,
            VariantCount = variantCount,
            OperationCount = routingOps.Count,
            TimeUnit = "MIN",
            TotalRoutingTimeMinutes = totalRoutingTime,
            BaseBom = baseBom,
            RoutingOperations = routingOps,
            SelectedSizes = request.Sizes.Select(s => s.Size).ToList(),
            SelectedColors = request.Colors.Select(c => c.Color).ToList(),
        };
    }

    private static List<BomBaseRow> BuildBaseBom(
        IReadOnlyList<SimulationBomLineDto> templateBom,
        IReadOnlyList<SimulationArticleDto> articles,
        double sizeCoefficient)
    {
        var rows = new List<BomBaseRow>();
        foreach (var line in templateBom.OrderBy(l => l.ComponentCode))
        {
            var component = articles.FirstOrDefault(a => a.Id == line.ComponentArticleId);
            var net = line.ApplySizeCoefficient
                ? line.QuantityPer * sizeCoefficient
                : line.QuantityPer;
            var gross = line.ScrapRate > 0
                ? GenerationEngine.CalculateGrossQuantity(net, line.ScrapRate)
                : net;

            rows.Add(new BomBaseRow(
                line.ComponentCode,
                component?.Designation ?? line.ComponentCode,
                net,
                gross,
                component?.Unit ?? "PCS"));
        }

        return rows;
    }
}

public sealed record GammesSimulationContext(
    int SimulationId,
    IReadOnlyList<SimulationArticleDto> Articles,
    DuplicationOptionsDto DuplicationOptions);
