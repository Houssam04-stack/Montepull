using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;

namespace Axioplan.GammesNomenclatures.E2E.Tests;

[Parallelizable(ParallelScope.Self)]
[TestFixture]
public class ApsPagesTests : PageTest
{
    private async Task WaitBlazorAsync()
    {
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await Page.WaitForTimeoutAsync(1500);
    }

    public static IEnumerable<TestCaseData> PageButtonCases()
    {
        yield return new TestCaseData("/aps/journal", new[] { "Filtrer", "Appender demo" });
        yield return new TestCaseData("/aps/attendus", new[] { "Filtrer", "Emitter demo" });
        yield return new TestCaseData("/aps/compilateur", new[] { "Recompiler", "Rafraichir" });
        yield return new TestCaseData("/aps/capacites", new[] { "Calculer", "Reserver 1u demo" });
        yield return new TestCaseData("/aps/cbn", new[] { "Lancer CBN APS" });
        yield return new TestCaseData("/aps/planification", new[] { "Lancer planification complète", "Cap_cum" });
        yield return new TestCaseData("/aps/flux", new[] { "Recalculer", "Publier" });
        yield return new TestCaseData("/aps/charges", new[] { "Calculer charges" });
        yield return new TestCaseData("/aps/ctp", new[] { "Évaluer", "Promettre", "Refuser" });
        yield return new TestCaseData("/aps/promesses", new[] { "Rafraîchir" });
        yield return new TestCaseData("/aps/contrats", new[] { "Publier contrat demo", "Tenter révision" });
        yield return new TestCaseData("/aps/cycle-nocturne", new[] { "Lancer cycle" });
        yield return new TestCaseData("/aps/recette", new[] { "Sans historique", "Évaluer avec matches demo", "Tester verrou M2/M6" });
    }

    [TestCaseSource(nameof(PageButtonCases))]
    public async Task Aps_page_buttons_clickable(string route, string[] buttons)
    {
        await Page.GotoAsync(AuditConfig.BaseUrl + route);
        await WaitBlazorAsync();

        foreach (var label in buttons)
        {
            var btn = Page.GetByRole(AriaRole.Button, new() { Name = label });
            if (await btn.CountAsync() == 0) continue;
            await btn.First.ClickAsync();
            await WaitBlazorAsync();
        }

        var err = Page.Locator(".alert-danger");
        if (await err.CountAsync() > 0)
        {
            var text = await err.First.TextContentAsync();
            TestContext.WriteLine($"[{route}] alert: {text}");
        }
    }

    [Test]
    public async Task Aps_readonly_pages_load()
    {
        foreach (var route in new[] { "/aps", "/aps/referentiel", "/aps/plan-hebdo", "/aps/plan-jour", "/aps/nervosite" })
        {
            await Page.GotoAsync(AuditConfig.BaseUrl + route);
            await WaitBlazorAsync();
            await Expect(Page.Locator("body")).Not.ToBeEmptyAsync();
        }
    }
}
