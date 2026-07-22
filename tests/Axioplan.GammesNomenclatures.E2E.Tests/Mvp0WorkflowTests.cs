using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;

namespace Axioplan.GammesNomenclatures.E2E.Tests;

[Parallelizable(ParallelScope.Self)]
[TestFixture]
public class Mvp0WorkflowTests : PageTest
{
    private async Task WaitBlazorAsync()
    {
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await Page.WaitForTimeoutAsync(1500);
    }

    [Test]
    public async Task Simulated_campaign_demo_end_to_end_gate_insufficient_data()
    {
        await Page.GotoAsync(AuditConfig.BaseUrl + "/mvp0");
        await WaitBlazorAsync();

        await Page.Locator("input[type=checkbox]").CheckAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Parcours demo bout-en-bout" }).ClickAsync();
        await Page.WaitForURLAsync("**/mvp0/workflow/**", new() { Timeout = 120_000 });
        await WaitBlazorAsync();

        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Gate — INSUFFICIENT_DATA" }))
            .ToBeVisibleAsync(new() { Timeout = 120_000 });

        var gate = await SqlAuditHelper.ScalarStringAsync(
            "SELECT TOP 1 gate_outcome FROM mvp0_campaigns ORDER BY created_at DESC");
        Assert.That(gate, Is.EqualTo("INSUFFICIENT_DATA"));

        var campaigns = await SqlAuditHelper.ScalarIntAsync("SELECT COUNT(*) FROM mvp0_campaigns");
        Assert.That(campaigns, Is.GreaterThan(0));
        var batches = await SqlAuditHelper.ScalarIntAsync("SELECT COUNT(*) FROM mvp0_import_batches");
        Assert.That(batches, Is.GreaterThan(0));
        var anomalies = await SqlAuditHelper.ScalarIntAsync("SELECT COUNT(*) FROM mvp0_anomalies");
        Assert.That(anomalies, Is.GreaterThan(0));
        var bypasses = await SqlAuditHelper.ScalarIntAsync("SELECT COUNT(*) FROM mvp0_bypasses");
        Assert.That(bypasses, Is.GreaterThan(0));
    }

    [Test]
    public async Task Real_campaign_clean_seed_workflow_can_reach_gate()
    {
        var family = "AUDIT" + DateTime.UtcNow.Ticks % 10000;
        await Page.GotoAsync(AuditConfig.BaseUrl + "/mvp0");
        await WaitBlazorAsync();

        await Page.Locator("input").First.FillAsync(family);
        var checkbox = Page.Locator("input[type=checkbox]");
        if (await checkbox.IsCheckedAsync()) await checkbox.UncheckAsync();

        await Page.GetByRole(AriaRole.Button, new() { Name = "Créer" }).ClickAsync();
        await Page.WaitForURLAsync("**/mvp0/workflow/**", new() { Timeout = 60_000 });
        await WaitBlazorAsync();

        await Page.GetByRole(AriaRole.Button, new() { Name = "2b. Import seed REAL propre" }).ClickAsync();
        await WaitBlazorAsync();
        await Expect(Page.Locator(".alert-success, .alert-danger")).ToBeVisibleAsync(new() { Timeout = 60_000 });

        await Page.GetByRole(AriaRole.Button, new() { Name = "3. Validation" }).ClickAsync();
        await WaitBlazorAsync();

        await Page.GetByRole(AriaRole.Button, new() { Name = "5. Score intrant" }).ClickAsync();
        await WaitBlazorAsync();

        await Page.GetByRole(AriaRole.Button, new() { Name = "6–7. Backtest + score résultat" }).ClickAsync();
        await WaitBlazorAsync();

        await Page.GetByRole(AriaRole.Button, new() { Name = "8. Enregistrer avis" }).ClickAsync();
        await WaitBlazorAsync();

        await Page.GetByRole(AriaRole.Button, new() { Name = "9. Gate" }).ClickAsync();
        await WaitBlazorAsync();

        await Page.GetByRole(AriaRole.Button, new() { Name = "10. Rapport" }).ClickAsync();
        await WaitBlazorAsync();

        var gate = await SqlAuditHelper.ScalarStringAsync(
            "SELECT TOP 1 gate_outcome FROM mvp0_campaigns WHERE family_code LIKE 'AUDIT%' ORDER BY created_at DESC");
        Assert.That(gate, Is.Not.Null.And.Not.EqualTo("INSUFFICIENT_DATA"));

        var report = await SqlAuditHelper.ScalarIntAsync(
            "SELECT COUNT(*) FROM mvp0_reports WHERE campaign_id = (SELECT TOP 1 id FROM mvp0_campaigns WHERE family_code LIKE 'AUDIT%' ORDER BY created_at DESC)");
        Assert.That(report, Is.GreaterThan(0));
    }

    [Test]
    public async Task Backtest_blocked_when_blocking_without_bypass()
    {
        var family = "BLK" + DateTime.UtcNow.Ticks % 10000;
        await Page.GotoAsync(AuditConfig.BaseUrl + "/mvp0");
        await WaitBlazorAsync();

        await Page.Locator("input").First.FillAsync(family);
        var checkbox = Page.Locator("input[type=checkbox]");
        if (!await checkbox.IsCheckedAsync()) await checkbox.CheckAsync();

        await Page.GetByRole(AriaRole.Button, new() { Name = "Créer" }).ClickAsync();
        await Page.WaitForURLAsync("**/mvp0/workflow/**");
        await WaitBlazorAsync();

        await Page.GetByRole(AriaRole.Button, new() { Name = "2. Import seed anomalies" }).ClickAsync();
        await WaitBlazorAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "3. Validation" }).ClickAsync();
        await WaitBlazorAsync();

        await Page.GetByRole(AriaRole.Button, new() { Name = "6–7. Backtest + score résultat" }).ClickAsync();
        await WaitBlazorAsync();

        var err = Page.Locator(".alert-danger");
        await Expect(err).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Expect(err).ToContainTextAsync("BLOCKING", new() { Timeout = 5_000 });
    }

    [Test]
    public async Task Mvp0_imports_csv_preview_and_seed_buttons()
    {
        await Page.GotoAsync(AuditConfig.BaseUrl + "/mvp0/imports");
        await WaitBlazorAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Assistant CSV" })).ToBeVisibleAsync();
    }
}
