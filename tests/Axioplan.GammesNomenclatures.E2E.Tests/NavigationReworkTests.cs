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
        "/imports",
        "/mvp0",
        "/simulation-mrp",
        "/cbn/legacy",
        "/consultation",
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
    public async Task Unified_cbn_opens_tunimapulf_simulation()
    {
        await Page.GotoAsync(AuditConfig.BaseUrl + "/simulation-mrp?ensure=tunimapulf", new() { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 60_000 });
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Duplication", Exact = true })).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "CBN", Exact = true })).ToBeVisibleAsync();
        await Expect(Page.Locator("body")).ToContainTextAsync("TUNIMAPULF");
    }
    [Test]
    public async Task Menu_shows_unified_cbn_entry()
    {
        await Page.GotoAsync(AuditConfig.BaseUrl + "/accueil", new() { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 60_000 });
        await Expect(Page.Locator(".sidebar-nav a", new() { HasText = "CBN — Duplication, commandes & calcul" })).ToBeVisibleAsync();
        await Expect(Page.Locator(".sidebar-nav a", new() { HasText = "Simulation MRP — Duplication & CBN" })).ToHaveCountAsync(0);
        await Expect(Page.Locator(".sidebar-nav a", new() { HasText = "CBN métier — Commandes & calcul" })).ToHaveCountAsync(0);
        await Expect(Page.Locator(".sidebar-nav a", new() { HasText = "Paramètres" })).ToHaveCountAsync(0);
    }

    [Test]
    public async Task Menu_unified_cbn_opens_simulation_and_metier_tabs()
    {
        await Page.GotoAsync(AuditConfig.BaseUrl + "/accueil", new() { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 60_000 });
        await Page.Locator(".sidebar-nav a", new() { HasText = "CBN — Duplication, commandes & calcul" }).ClickAsync();
        await Page.WaitForURLAsync(url => url.Contains("simulation-mrp", StringComparison.OrdinalIgnoreCase), new() { Timeout = 60_000 });
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Duplication", Exact = true })
            .Or(Page.GetByRole(AriaRole.Button, new() { Name = "Recreer TUNIMAPULF" }))
            .First).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Métier — Commandes & calcul" })).ToBeVisibleAsync();
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
        await Page.WaitForURLAsync(url => url.Contains("simulation-mrp", StringComparison.OrdinalIgnoreCase), new() { Timeout = 60_000 });
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Métier — Commandes & calcul" })).ToBeVisibleAsync();
    }
}
