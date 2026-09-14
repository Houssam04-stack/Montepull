using Axioplan.GammesNomenclatures.Application.Abstractions;
using Axioplan.GammesNomenclatures.Application.Mvp0;
using Axioplan.GammesNomenclatures.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Axioplan.GammesNomenclatures.Domain.Tests;

public sealed class Mvp0ContextSqlFactAttribute : FactAttribute
{
    public Mvp0ContextSqlFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("AXIOPLAN_CONNECTION_STRING_DOTNET")))
            Skip = "Requires explicitly configured isolated SQL database.";
    }
}

public sealed class Mvp0ContextSqlIntegrationTests
{
    private static ServiceProvider Services()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>
        { ["Database:ConnectionString"] = Environment.GetEnvironmentVariable("AXIOPLAN_CONNECTION_STRING_DOTNET") }).Build();
        return new ServiceCollection().AddInfrastructure(config).AddMvp0Application().BuildServiceProvider();
    }

    private static async Task<object?> Scalar(string sql)
    {
        await using var c = new SqlConnection(Environment.GetEnvironmentVariable("AXIOPLAN_CONNECTION_STRING_DOTNET"));
        await c.OpenAsync(); await using var cmd = new SqlCommand(sql, c); return await cmd.ExecuteScalarAsync();
    }

    private static async Task<(int Cbn, int Pegging)> Fixture()
    {
        var cbn = Convert.ToInt32(await Scalar("""
            INSERT cbn_runs(sales_order_id,product_family_id,status,notes)
            OUTPUT inserted.id
            SELECT TOP(1) sales_order_id,product_family_id,'COMPLETED','MVP0_CONTEXT_TEST'
            FROM cbn_runs WHERE EXISTS(SELECT 1 FROM cbn_flattened_bom_lines b WHERE b.cbn_run_id=cbn_runs.id)
            ORDER BY id;
            """));
        Assert.True(cbn > 0, "Initialize repository demo CBN data first.");
        await Scalar($"""
            INSERT cbn_flattened_bom_lines(cbn_run_id,parent_article_id,component_article_id,bom_line_no,bom_level,quantity_per_unit,unit,loss_rate,behavior,source_path)
            SELECT {cbn},parent_article_id,component_article_id,bom_line_no,bom_level,quantity_per_unit,unit,loss_rate,behavior,source_path
            FROM cbn_flattened_bom_lines WHERE cbn_run_id=(SELECT MIN(cbn_run_id) FROM cbn_flattened_bom_lines);
            """);
        var pegging = Convert.ToInt32(await Scalar($"INSERT pegging_runs(cbn_run_id,status,notes) OUTPUT inserted.id VALUES({cbn},'COMPLETED','MVP0_CONTEXT_TEST')"));
        return (cbn,pegging);
    }

    [Mvp0ContextSqlFact]
    public async Task Linked_campaign_is_idempotent_persisted_and_does_not_modify_cbn_pegging()
    {
        using var services=Services(); var context=services.GetRequiredService<Mvp0ContextService>();
        var f=await Fixture(); var before=await Scalar($"SELECT COUNT(*) FROM cbn_flattened_bom_lines WHERE cbn_run_id={f.Cbn}");
        var campaign=await context.ContinueAsync(f.Cbn,f.Pegging);
        var again=await context.ContinueAsync(f.Cbn,f.Pegging);
        Assert.Equal(campaign,again);
        using var reloaded=Services();
        var association=await reloaded.GetRequiredService<Mvp0ContextService>().GetCampaignContextAsync(campaign);
        Assert.NotNull(association); Assert.Equal(f.Cbn,association.CbnRunId); Assert.Equal(f.Pegging,association.PeggingRunId);
        Assert.Equal(before,await Scalar($"SELECT COUNT(*) FROM cbn_flattened_bom_lines WHERE cbn_run_id={f.Cbn}"));
        Assert.Equal(0,Convert.ToInt32(await Scalar($"SELECT COUNT(*) FROM pegging_links WHERE pegging_run_id={f.Pegging}")));
        Assert.Equal("COMPLETED",await Scalar($"SELECT status FROM cbn_runs WHERE id={f.Cbn}"));
    }

    [Mvp0ContextSqlFact]
    public async Task Snapshot_import_is_explicit_and_uses_actual_cbn_bom()
    {
        using var services=Services(); var context=services.GetRequiredService<Mvp0ContextService>();
        var repository=services.GetRequiredService<IMvp0Repository>(); var f=await Fixture();
        var campaign=await context.ContinueAsync(f.Cbn,f.Pegging);
        var empty=await repository.LoadWorkingSetAsync(campaign); Assert.Empty(empty.Articles);
        await context.ImportContextAsync(campaign);
        var snapshot=await repository.LoadWorkingSetAsync(campaign);
        Assert.NotEmpty(snapshot.Articles); Assert.NotEmpty(snapshot.Boms); Assert.Empty(snapshot.Wos);
        Assert.All(snapshot.Articles,a=>Assert.Equal("SIMULATED",a.Provenance));
        Assert.Equal(Convert.ToInt32(await Scalar($"SELECT COUNT(*) FROM cbn_flattened_bom_lines WHERE cbn_run_id={f.Cbn}")),snapshot.Boms.Count);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>context.ImportContextAsync(campaign));
    }

    [Mvp0ContextSqlFact]
    public async Task Linked_simulated_gate_stays_insufficient_after_full_quality_workflow()
    {
        using var services=Services(); var context=services.GetRequiredService<Mvp0ContextService>();
        var workflow=services.GetRequiredService<Mvp0WorkflowService>(); var f=await Fixture();
        var campaign=await context.ContinueAsync(f.Cbn,f.Pegging); await context.ImportContextAsync(campaign);
        var anomalies=await workflow.RunValidationAsync(campaign);
        foreach(var a in anomalies.Where(a=>a.Severity=="BLOCKING"))
            await workflow.CreateBypassAsync(campaign,a.AnomalyId,"SQL integration test — missing source data acknowledged","QA");
        await workflow.ComputeInputReliabilityAsync(campaign); await workflow.RunBacktestAsync(campaign);
        await workflow.SavePlannerReviewAsync(campaign,"QA","Actual snapshot, no fabricated OF history",true);
        var gate=await workflow.RunGateAsync(campaign); Assert.Equal("INSUFFICIENT_DATA",gate.Outcome);
        Assert.True(gate.HasSimulatedData);
        await Scalar($"UPDATE dbo.mvp0_campaign_contexts SET payload_json=JSON_MODIFY(payload_json,'$.OrderCode','QA_LABEL_ONLY') WHERE campaign_id='{campaign}'");
        Assert.Equal("QA_LABEL_ONLY",(await context.GetCampaignContextAsync(campaign))!.OrderCode);
        var afterMetadata=await workflow.RunGateAsync(campaign);
        Assert.Equal(gate.Outcome,afterMetadata.Outcome); Assert.Equal(gate.InputScore,afterMetadata.InputScore);
        Assert.Equal(gate.ResultScore,afterMetadata.ResultScore); Assert.Equal(gate.HasSimulatedData,afterMetadata.HasSimulatedData);
        var report=await workflow.BuildReportAsync(campaign); Assert.Contains("Contexte",report);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>workflow.ImportCleanRealSeedAsync(campaign));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>workflow.ImportCsvDemoSeedAsync(campaign));
    }

    [Mvp0ContextSqlFact]
    public async Task Invalid_or_mismatched_run_ids_do_not_create_campaigns()
    {
        using var services=Services(); var context=services.GetRequiredService<Mvp0ContextService>(); var f=await Fixture();
        var before=await Scalar("SELECT COUNT(*) FROM mvp0_campaigns");
        await Assert.ThrowsAsync<InvalidOperationException>(()=>context.ContinueAsync(-1,f.Pegging));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>context.ContinueAsync(f.Cbn,-1));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>context.ContinueAsync(f.Cbn+1,f.Pegging));
        Assert.Equal(before,await Scalar("SELECT COUNT(*) FROM mvp0_campaigns"));
    }

    [Mvp0ContextSqlFact]
    public async Task Old_and_standalone_campaigns_load_without_context()
    {
        using var services=Services(); var context=services.GetRequiredService<Mvp0ContextService>();
        var workflow=services.GetRequiredService<Mvp0WorkflowService>(); await workflow.EnsureReadyAsync();
        var old=Guid.Parse("11111111-1111-1111-1111-111111111101");
        Assert.Null(await context.GetCampaignContextAsync(old)); Assert.NotNull(await workflow.GetStateAsync(old));
        var standalone=await workflow.CreateCampaignAsync("CTX"+Guid.NewGuid().ToString("N")[..8],"SITE-1","QA",DateOnly.FromDateTime(DateTime.Today.AddDays(-90)),DateOnly.FromDateTime(DateTime.Today),false);
        Assert.Null(await context.GetCampaignContextAsync(standalone.Id));
        await workflow.ImportCleanRealSeedAsync(standalone.Id); await workflow.RunValidationAsync(standalone.Id);
        await workflow.ComputeInputReliabilityAsync(standalone.Id); await workflow.RunBacktestAsync(standalone.Id);
        await workflow.SavePlannerReviewAsync(standalone.Id,"QA","Clean standalone regression",true);
        Assert.Equal("GO",(await workflow.RunGateAsync(standalone.Id)).Outcome);
    }

    [Mvp0ContextSqlFact]
    public async Task Context_foreign_keys_have_no_cascade_and_protect_associations()
    {
        using var services=Services(); var context=services.GetRequiredService<Mvp0ContextService>(); var f=await Fixture();
        var campaign=await context.ContinueAsync(f.Cbn,f.Pegging);
        Assert.Equal(3,Convert.ToInt32(await Scalar("SELECT COUNT(*) FROM sys.foreign_keys WHERE parent_object_id=OBJECT_ID('dbo.mvp0_campaign_contexts') AND delete_referential_action=0")));
        await Assert.ThrowsAsync<SqlException>(()=>Scalar($"DELETE pegging_runs WHERE id={f.Pegging}"));
        Assert.NotNull(await context.GetCampaignContextAsync(campaign));
        Assert.Equal(1,Convert.ToInt32(await Scalar($"SELECT COUNT(*) FROM pegging_runs WHERE id={f.Pegging}")));
    }
}
