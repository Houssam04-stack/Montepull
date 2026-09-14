using System.Data;
using System.Text.Json;
using Axioplan.GammesNomenclatures.Application.Abstractions;
using Axioplan.GammesNomenclatures.Application.Models;
using Axioplan.GammesNomenclatures.Application.Mvp0;
using Axioplan.GammesNomenclatures.Domain.Mvp0;
using Microsoft.Data.SqlClient;
namespace Axioplan.GammesNomenclatures.Infrastructure.Mvp0;
public sealed class SqlServerMvp0ContextRepository(Mvp0SchemaBootstrap schema):IMvp0ContextRepository
{
 private static readonly SemaphoreSlim SchemaGate=new(1,1);
 private async Task Ensure(CancellationToken ct)
 {
  await schema.EnsureAsync(ct); await SchemaGate.WaitAsync(ct);
  try { await using var c=schema.OpenConnection(); await using var cmd=c.CreateCommand(); cmd.CommandText=SchemaSql; await cmd.ExecuteNonQueryAsync(ct); }
  finally {SchemaGate.Release();}
 }
 private const string SchemaSql="""
 IF OBJECT_ID(N'dbo.mvp0_campaign_contexts',N'U') IS NULL
 CREATE TABLE dbo.mvp0_campaign_contexts(
 campaign_id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,cbn_run_id INT NOT NULL,pegging_run_id INT NOT NULL UNIQUE,
 payload_json NVARCHAR(MAX) NOT NULL,created_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
 CONSTRAINT FK_mvp0_context_campaign FOREIGN KEY(campaign_id) REFERENCES dbo.mvp0_campaigns(id),
 CONSTRAINT FK_mvp0_context_cbn FOREIGN KEY(cbn_run_id) REFERENCES dbo.cbn_runs(id),
 CONSTRAINT FK_mvp0_context_pegging FOREIGN KEY(pegging_run_id) REFERENCES dbo.pegging_runs(id));
 """;
 private static async Task<Mvp0BusinessContext?> ReadContext(SqlConnection c,SqlTransaction? tx,int cbn,int? peg,CancellationToken ct)
 {
  await using var cmd=new SqlCommand("""
  SELECT TOP(1) r.id,p.id,p.version_no,o.code,f.code,o.data_source,o.import_batch_id,
  COALESCE((SELECT STRING_AGG(a.code,',') FROM articles a WHERE EXISTS(SELECT 1 FROM sales_order_lines l WHERE l.sales_order_id=o.id AND l.article_id=a.id)),'')
  FROM cbn_runs r JOIN sales_orders o ON o.id=r.sales_order_id JOIN product_families f ON f.id=r.product_family_id
  JOIN pegging_runs p ON p.cbn_run_id=r.id
  WHERE r.id=@cbn AND r.status='COMPLETED' AND p.status='COMPLETED' AND (@peg IS NULL OR p.id=@peg)
  ORDER BY p.version_no DESC,p.id DESC
  """,c,tx);
  cmd.Parameters.AddWithValue("@cbn",cbn); cmd.Parameters.AddWithValue("@peg",(object?)peg??DBNull.Value);
  await using var r=await cmd.ExecuteReaderAsync(ct); if(!await r.ReadAsync(ct))return null;
  return new(r.GetInt32(0),r.GetInt32(1),r.GetInt32(2),r.GetString(3),r.GetString(4),r.GetString(7).Split(',',StringSplitOptions.RemoveEmptyEntries),r.IsDBNull(5)?null:r.GetString(5),r.IsDBNull(6)?null:r.GetInt64(6));
 }
 public async Task<Mvp0BusinessContext?> GetRunContextAsync(int cbn,int? pegging,CancellationToken ct=default)
 {await Ensure(ct); await using var c=schema.OpenConnection();return await ReadContext(c,null,cbn,pegging,ct);}
 public async Task<Mvp0BusinessContext?> GetCampaignContextAsync(Guid id,CancellationToken ct=default)
 {
  await Ensure(ct);await using var c=schema.OpenConnection();await using var cmd=new SqlCommand("SELECT payload_json FROM dbo.mvp0_campaign_contexts WHERE campaign_id=@id",c);
  cmd.Parameters.AddWithValue("@id",id);var json=await cmd.ExecuteScalarAsync(ct); return json is string s?JsonSerializer.Deserialize<Mvp0BusinessContext>(s):null;
 }
 public async Task<Guid> CreateOrGetCampaignAsync(Mvp0Campaign campaign,int cbn,int pegging,CancellationToken ct=default)
 {
  await Ensure(ct);await using var c=schema.OpenConnection();await using var tx=(SqlTransaction)await c.BeginTransactionAsync(IsolationLevel.Serializable,ct);
  var context=await ReadContext(c,tx,cbn,pegging,ct)??throw new InvalidOperationException("Contexte CBN/pegging invalide.");
  await using var cmd=new SqlCommand("SELECT campaign_id FROM dbo.mvp0_campaign_contexts WITH(UPDLOCK,HOLDLOCK) WHERE pegging_run_id=@peg",c,tx);
  cmd.Parameters.AddWithValue("@peg",pegging);var existing=await cmd.ExecuteScalarAsync(ct);
  if(existing is Guid saved){await tx.CommitAsync(ct);return saved;}
  cmd.CommandText="""
  INSERT dbo.mvp0_campaigns(id,code,family_code,site_code,period_from,period_to,owner_name,status,data_source,provenance,created_at,import_version,go_threshold,go_res_threshold,gate_outcome)
  VALUES(@id,@code,@fam,@site,@from,@to,@owner,'DRAFT',@source,@prov,@at,0,85,70,NULL);
  INSERT dbo.mvp0_campaign_contexts(campaign_id,cbn_run_id,pegging_run_id,payload_json) VALUES(@id,@cbn,@peg,@json);
  """;
  cmd.Parameters.AddWithValue("@id",campaign.Id);cmd.Parameters.AddWithValue("@code",campaign.Code);cmd.Parameters.AddWithValue("@fam",context.FamilyCode);cmd.Parameters.AddWithValue("@site",campaign.SiteCode);
  cmd.Parameters.AddWithValue("@from",campaign.PeriodFrom.ToDateTime(TimeOnly.MinValue));cmd.Parameters.AddWithValue("@to",campaign.PeriodTo.ToDateTime(TimeOnly.MinValue));cmd.Parameters.AddWithValue("@owner",campaign.Owner);
  cmd.Parameters.AddWithValue("@source",context.SourceDataset??"CBN_CONTEXT");cmd.Parameters.AddWithValue("@prov",Mvp0ContextService.GetProvenance(context.SourceDataset));cmd.Parameters.AddWithValue("@at",campaign.CreatedAtUtc);
  cmd.Parameters.AddWithValue("@cbn",cbn);cmd.Parameters.AddWithValue("@json",JsonSerializer.Serialize(context));await cmd.ExecuteNonQueryAsync(ct);await tx.CommitAsync(ct);return campaign.Id;
 }
 public async Task<Mvp0ContextSnapshot> LoadSnapshotAsync(int cbn,CancellationToken ct=default)
 {
  await Ensure(ct);await using var c=schema.OpenConnection();await using var cmd=new SqlCommand("""
  SELECT a.code,a.article_type,a.default_unit,af.code,a.status
  FROM articles a LEFT JOIN article_families af ON af.id=a.family_id
  WHERE EXISTS(SELECT 1 FROM cbn_flattened_bom_lines b WHERE b.cbn_run_id=@cbn AND(a.id=b.parent_article_id OR a.id=b.component_article_id))
  OR EXISTS(SELECT 1 FROM sales_order_lines l JOIN cbn_runs r ON r.sales_order_id=l.sales_order_id WHERE r.id=@cbn AND l.article_id=a.id);
  SELECT pa.code,ca.code,b.quantity_per_unit,b.unit,b.loss_rate,b.bom_line_no
  FROM cbn_flattened_bom_lines b JOIN articles pa ON pa.id=b.parent_article_id JOIN articles ca ON ca.id=b.component_article_id WHERE b.cbn_run_id=@cbn ORDER BY b.id;
  """,c);
  cmd.Parameters.AddWithValue("@cbn",cbn);await using var reader=await cmd.ExecuteReaderAsync(ct);var articles=new List<Mvp0ArticleRow>();var boms=new List<Mvp0BomRow>();
  while(await reader.ReadAsync(ct))articles.Add(new(reader.GetString(0),reader.GetString(1),reader.GetString(2),reader.IsDBNull(3)?null:reader.GetString(3),reader.GetString(4)=="ACTIVE","SIMULATED",articles.Count+1));
  await reader.NextResultAsync(ct);while(await reader.ReadAsync(ct))boms.Add(new(reader.GetString(0),reader.GetString(1),Convert.ToDouble(reader.GetValue(2)),reader.GetString(3),Convert.ToDouble(reader.GetValue(4)),reader.GetInt32(5)));
  return new(articles,boms);
 }
}
