using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;
namespace Axioplan.GammesNomenclatures.E2E.Tests;
[TestFixture]
public class Mvp0ContextJourneyTests:PageTest
{
 [Test]
 public async Task Pegging_context_opens_same_persisted_campaign_on_reload()
 {
  var cbn=await SqlAuditHelper.ScalarIntAsync("SELECT TOP(1) r.id FROM cbn_runs r JOIN sales_orders o ON o.id=r.sales_order_id WHERE o.code='DOUBLYGILF-TEST' AND EXISTS(SELECT 1 FROM pegging_runs p WHERE p.cbn_run_id=r.id AND p.status='COMPLETED') ORDER BY r.id");
  await Page.GotoAsync($"{AuditConfig.BaseUrl}/pegging/legacy?cbnRunId={cbn}");
  await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);await Page.WaitForTimeoutAsync(1200);
  await Expect(Page.GetByLabel("Run CBN",new(){Exact=true})).ToHaveValueAsync(cbn.ToString());
  await Page.GetByRole(AriaRole.Button,new(){Name="Continuer vers MVP-0",Exact=true}).ClickAsync();
  await Page.WaitForURLAsync("**/mvp0/workflow/**");
  await Expect(Page.GetByRole(AriaRole.Heading,new(){Name="Contexte de la campagne"})).ToBeVisibleAsync();
  var url=Page.Url;await Page.ReloadAsync();
  await Expect(Page.GetByRole(AriaRole.Heading,new(){Name="Contexte de la campagne"})).ToBeVisibleAsync();
  await Expect(Page.GetByRole(AriaRole.Button,new(){Name="2b. Import seed REAL propre"})).ToHaveCountAsync(0);
  await Page.GotoAsync($"{AuditConfig.BaseUrl}/pegging/legacy?cbnRunId={cbn}");
  await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);await Page.WaitForTimeoutAsync(1200);
  await Page.GetByRole(AriaRole.Button,new(){Name="Continuer vers MVP-0",Exact=true}).ClickAsync();
  await Page.WaitForURLAsync(url);
 }
 [Test]
 public async Task Invalid_cbn_query_displays_error_without_transfer_action()
 {
  await Page.GotoAsync(AuditConfig.BaseUrl+"/pegging/legacy?cbnRunId=-1");
  await Expect(Page.Locator(".alert-danger")).ToContainTextAsync("Run CBN introuvable");
  await Expect(Page.GetByRole(AriaRole.Button,new(){Name="Continuer vers MVP-0"})).ToHaveCountAsync(0);
 }
 [Test]
 public async Task Persisted_planner_review_is_restored_in_form_after_reload()
 {
  var id=await SqlAuditHelper.ScalarStringAsync("SELECT TOP(1) CONVERT(varchar(36),c.campaign_id) FROM mvp0_campaign_contexts c JOIN mvp0_scores s ON s.campaign_id=c.campaign_id WHERE s.planner_name IS NOT NULL ORDER BY c.created_at DESC");
  Assert.That(id,Is.Not.Null.And.Not.Empty,"A persisted linked planner review is required.");
  var campaign=Guid.Parse(id!);
  var name=await SqlAuditHelper.ScalarStringAsync($"SELECT planner_name FROM mvp0_scores WHERE campaign_id='{campaign}'");
  var comment=await SqlAuditHelper.ScalarStringAsync($"SELECT COALESCE(planner_comment,'') FROM mvp0_scores WHERE campaign_id='{campaign}'");
  var approved=await SqlAuditHelper.ScalarIntAsync($"SELECT CONVERT(int,planner_approved) FROM mvp0_scores WHERE campaign_id='{campaign}'");
  await Page.GotoAsync($"{AuditConfig.BaseUrl}/mvp0/workflow/{campaign}");
  await Expect(Page.Locator(".form-grid input").Nth(0)).ToHaveValueAsync(name!);
  await Expect(Page.Locator(".form-grid input").Nth(1)).ToHaveValueAsync(comment!);
  Assert.That(await Page.GetByRole(AriaRole.Checkbox,new(){Name="Approuvé"}).IsCheckedAsync(),Is.EqualTo(approved==1));
  await Page.ReloadAsync();
  await Expect(Page.Locator(".form-grid input").Nth(1)).ToHaveValueAsync(comment!);
  Assert.That(await Page.GetByRole(AriaRole.Checkbox,new(){Name="Approuvé"}).IsCheckedAsync(),Is.EqualTo(approved==1));
 }
}
