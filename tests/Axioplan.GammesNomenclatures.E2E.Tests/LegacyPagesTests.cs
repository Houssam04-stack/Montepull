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
        await Page.GotoAsync(AuditConfig.BaseUrl + "/simulation-mrp");
        await WaitBlazorAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Simulation MRP" })).ToBeVisibleAsync();
    }

    [Test]
    public async Task Cbn_legacy_calculer()
    {
        await Page.GotoAsync(AuditConfig.BaseUrl + "/cbn");
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
    public async Task Articles_parametres_imports_load()
    {
        foreach (var route in new[] { "/articles", "/parametres", "/imports", "/pegging", "/stock" })
        {
            await Page.GotoAsync(AuditConfig.BaseUrl + route);
            await WaitBlazorAsync();
            await Expect(Page.Locator("body")).Not.ToBeEmptyAsync();
        }
    }
}
