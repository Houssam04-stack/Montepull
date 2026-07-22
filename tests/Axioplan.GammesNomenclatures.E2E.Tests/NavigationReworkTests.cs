using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;

namespace Axioplan.GammesNomenclatures.E2E.Tests;

[Parallelizable(ParallelScope.Self)]
[TestFixture]
public class NavigationReworkTests : PageTest
{
    public static IEnumerable<string> NewMenuRoutes =>
    [
        "/accueil",
        "/articles",
        "/nomenclatures-gammes",
        "/stock",
        "/parametres",
        "/imports",
        "/mvp0",
        "/cbn",
        "/pegging",
        "/charges-capacites",
        "/aps/planification",
        "/aps/journal",
        "/aps/attendus",
        "/admin/experimental",
        "/admin/documentation"
    ];

    [TestCaseSource(nameof(NewMenuRoutes))]
    public async Task New_menu_route_returns_200(string route)
    {
        var response = await Page.GotoAsync(AuditConfig.BaseUrl + route, new() { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 60_000 });
        Assert.That(response, Is.Not.Null);
        Assert.That(response!.Status, Is.EqualTo(200), $"Route {route}");
        await Expect(Page.Locator(".sidebar-nav")).ToBeVisibleAsync();
    }

    [Test]
    public async Task Dashboard_shows_module_cards()
    {
        await Page.GotoAsync(AuditConfig.BaseUrl + "/accueil", new() { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 60_000 });
        await Expect(Page.Locator(".module-card").First).ToBeVisibleAsync();
        await Expect(Page.Locator("h2", new() { HasText = "Tableau de bord" })).ToBeVisibleAsync();
    }

    [Test]
    public async Task Unified_cbn_shows_real_calculator()
    {
        await Page.GotoAsync(AuditConfig.BaseUrl + "/cbn", new() { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 60_000 });
        await Expect(Page.GetByText("Calculer les besoins")).ToBeVisibleAsync();
        await Expect(Page.Locator("h3", new() { HasText = "Lancer un calcul CBN" })).ToBeVisibleAsync();
    }

    [Test]
    public async Task Cbn_simulation_hub_still_shows_modes()
    {
        await Page.GotoAsync(AuditConfig.BaseUrl + "/cbn/simulation", new() { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 60_000 });
        await Expect(Page.Locator(".section-tab", new() { HasText = "Données réelles" })).ToBeVisibleAsync();
        await Expect(Page.Locator(".section-tab", new() { HasText = "Simulation et traçabilité" })).ToBeVisibleAsync();
        await Expect(Page.Locator(".section-tab", new() { HasText = "APS avancé" })).ToBeVisibleAsync();
    }

    [Test]
    public async Task Charges_capacites_has_tabs()
    {
        await Page.GotoAsync(AuditConfig.BaseUrl + "/charges-capacites", new() { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 60_000 });
        await Expect(Page.Locator(".section-tab", new() { HasText = "Capacité disponible" })).ToBeVisibleAsync();
        await Expect(Page.Locator(".section-tab", new() { HasText = "Charge calculée" })).ToBeVisibleAsync();
    }

    [Test]
    public async Task Articles_has_section_tabs()
    {
        await Page.GotoAsync(AuditConfig.BaseUrl + "/articles", new() { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 60_000 });
        await Expect(Page.Locator(".section-tab", new() { HasText = "Liste" })).ToBeVisibleAsync();
        await Expect(Page.Locator(".section-tab", new() { HasText = "Créer" })).ToBeVisibleAsync();
    }

    [Test]
    public async Task Legacy_cbn_route_still_works()
    {
        var response = await Page.GotoAsync(AuditConfig.BaseUrl + "/cbn/legacy", new() { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 60_000 });
        Assert.That(response!.Status, Is.EqualTo(200));
        await Expect(Page.GetByText("Calculer les besoins")).ToBeVisibleAsync();
    }
}
