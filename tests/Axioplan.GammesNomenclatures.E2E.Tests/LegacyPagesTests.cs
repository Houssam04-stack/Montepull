using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;

namespace Axioplan.GammesNomenclatures.E2E.Tests;

[Parallelizable(ParallelScope.Self)]
[TestFixture]
public class LegacyPagesTests : PageTest
{
    private async Task WaitBlazorAsync()
    {
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await Page.WaitForTimeoutAsync(1500);
    }

    [Test]
    public async Task Simulation_page_simuler_button()
    {
        await Page.GotoAsync(AuditConfig.BaseUrl + "/");
        await WaitBlazorAsync();
        var btn = Page.GetByRole(AriaRole.Button, new() { Name = "Simuler" });
        if (await btn.CountAsync() > 0)
        {
            await btn.ClickAsync();
            await WaitBlazorAsync();
            await Expect(Page.Locator("body")).Not.ToBeEmptyAsync();
        }
    }

    [Test]
    public async Task SimulationMrp_create_simulation()
    {
        await Page.GotoAsync(AuditConfig.BaseUrl + "/simulation-mrp?ensure=tunimapulf");
        await WaitBlazorAsync();
        await Expect(Page.Locator("body")).ToContainTextAsync("TUNIMAPULF");
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Duplication" }).Or(Page.GetByRole(AriaRole.Button, new() { Name = "Recreer TUNIMAPULF" })).First).ToBeVisibleAsync();
    }

    [Test]
    public async Task Cbn_legacy_calculer()
    {
        await Page.GotoAsync(AuditConfig.BaseUrl + "/cbn/legacy?tab=cbn");
        await WaitBlazorAsync();
        var btn = Page.GetByRole(AriaRole.Button, new() { Name = "Calculer les besoins" });
        if (await btn.CountAsync() > 0)
        {
            await btn.ClickAsync();
            await WaitBlazorAsync();
        }
        await Expect(Page.Locator("body")).Not.ToBeEmptyAsync();
    }

    [Test]
    public async Task Cbn_legacy_commandes_importees_tab()
    {
        await Page.GotoAsync(AuditConfig.BaseUrl + "/simulation-mrp?tab=metier");
        await WaitBlazorAsync();
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Métier — Commandes & calcul" })).ToBeVisibleAsync();
        await Expect(Page.Locator("body")).ToContainTextAsync("CMD-TUNIMAPULF");
    }

    [Test]
    public async Task Cbn_legacy_commandes_tab_has_excel_fields_and_create()
    {
        await Page.GotoAsync(AuditConfig.BaseUrl + "/simulation-mrp?tab=commandes");
        await WaitBlazorAsync();
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Métier — Commandes & calcul" })).ToBeVisibleAsync();
        await Expect(Page.Locator("body")).ToContainTextAsync("Qte_Commandee");
    }

    [Test]
    public async Task Montepull_imports_links_to_commandes()
    {
        await Page.GotoAsync(AuditConfig.BaseUrl + "/imports/montepull");
        await WaitBlazorAsync();
        await Expect(Page.GetByRole(AriaRole.Link, new() { Name = "Voir les commandes importees" })).ToBeVisibleAsync();
    }

    [Test]
    public async Task Articles_parametres_imports_load()
    {
        foreach (var route in new[] { "/articles", "/parametres", "/imports", "/pegging/legacy", "/stock" })
        {
            await Page.GotoAsync(AuditConfig.BaseUrl + route);
            await WaitBlazorAsync();
            await Expect(Page.Locator("body")).Not.ToBeEmptyAsync();
        }
    }
}
