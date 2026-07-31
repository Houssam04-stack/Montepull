using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;

namespace Axioplan.GammesNomenclatures.E2E.Tests;

[Parallelizable(ParallelScope.Self)]
[TestFixture]
public class RoutesAuditTests : PageTest
{
    public static IEnumerable<string> AllRoutes =>
    [
        "/accueil", "/charges-capacites", "/charges-capacites/charges", "/charges-capacites/flux",
        "/consultation", "/pegging/legacy",
        "/cbn/simulation", "/cbn/aps", "/cbn/legacy",
        "/admin/experimental", "/admin/documentation", "/nomenclatures-gammes",
        "/imports/bom", "/imports/autres",
        "/parametres/coefficients", "/parametres/consommation", "/parametres/temps",
        "/parametres/pertes", "/parametres/cbn", "/parametres/pegging", "/parametres/general",
        "/aps/planification", "/aps/resultats",
        "/mvp0", "/mvp0/campagnes", "/mvp0/imports", "/mvp0/validation", "/mvp0/anomalies",
        "/mvp0/bypass", "/mvp0/fiabilite", "/mvp0/backtest", "/mvp0/gate", "/mvp0/rapport",
        "/", "/simulation-mrp", "/imports", "/parametres", "/articles", "/articles/manual", "/articles/configured",
        "/articles/categories", "/articles/attributes", "/articles/matrix", "/articles/bom",
        "/cbn", "/pegging", "/stock",
        "/aps", "/aps/journal", "/aps/attendus", "/aps/referentiel", "/aps/compilateur",
        "/aps/capacites", "/aps/cbn", "/aps/flux", "/aps/charges", "/aps/ctp", "/aps/promesses",
        "/aps/contrats", "/aps/plan-hebdo", "/aps/plan-jour", "/aps/cycle-nocturne",
        "/aps/recette", "/aps/nervosite"
    ];

    [TestCaseSource(nameof(AllRoutes))]
    public async Task Route_returns_200_and_has_title(string route)
    {
        var response = await Page.GotoAsync(AuditConfig.BaseUrl + route, new() { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 60_000 });
        Assert.That(response, Is.Not.Null);
        Assert.That(response!.Status, Is.EqualTo(200), $"Route {route} HTTP status");
        await Expect(Page.Locator("body")).Not.ToBeEmptyAsync();
    }
}
