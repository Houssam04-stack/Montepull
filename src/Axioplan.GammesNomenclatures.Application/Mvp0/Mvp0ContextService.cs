using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Axioplan.GammesNomenclatures.Application.Abstractions;
using Axioplan.GammesNomenclatures.Application.Models;
using Axioplan.GammesNomenclatures.Domain.Mvp0;
namespace Axioplan.GammesNomenclatures.Application.Mvp0;
public sealed class Mvp0ContextService(IMvp0ContextRepository contexts,IMvp0Repository repository)
{
 public static string GetProvenance(string? source)=>source is "REAL" or "MONTEPULL_REAL" ? Mvp0Provenance.Real : Mvp0Provenance.Simulated;
 public Task<Mvp0BusinessContext?> GetCampaignContextAsync(Guid id,CancellationToken ct=default)=>contexts.GetCampaignContextAsync(id,ct);
 public async Task<Guid> ContinueAsync(int cbn,int? pegging=null,CancellationToken ct=default)
 {
  await repository.EnsureSchemaAsync(ct);
  var context=await contexts.GetRunContextAsync(cbn,pegging,ct) ?? throw new InvalidOperationException("Contexte invalide : sélectionner un CBN terminé et son pegging terminé.");
  var today=DateOnly.FromDateTime(DateTime.UtcNow);
  var campaign=new Mvp0Campaign(Guid.NewGuid(),$"MVP0-CBN{cbn}-PEG{context.PeggingRunId}",context.FamilyCode,"SITE-1",today.AddDays(-90),today,"Planificateur","DRAFT",context.SourceDataset??"CBN_CONTEXT",GetProvenance(context.SourceDataset),DateTime.UtcNow,0,85,70,null);
  return await contexts.CreateOrGetCampaignAsync(campaign,cbn,context.PeggingRunId,ct);
 }
 public async Task ImportContextAsync(Guid id,CancellationToken ct=default)
 {
  var context=await contexts.GetCampaignContextAsync(id,ct)??throw new InvalidOperationException("Campagne sans contexte CBN.");
  var working=await repository.LoadWorkingSetAsync(id,ct);
  if(working.Articles.Count+working.Boms.Count+working.Ops.Count+working.Calendars.Count+working.Wos.Count+working.Centers.Count+working.BomOpLinks.Count>0) throw new InvalidOperationException("Le jeu de travail existe déjà. L’import du contexte ne le remplace pas.");
  var campaign=await repository.GetCampaignAsync(id,ct)??throw new InvalidOperationException("Campagne introuvable.");
  var snapshot=await contexts.LoadSnapshotAsync(context.CbnRunId,ct);
  await repository.ReplaceWorkingSetAsync(id,snapshot.Articles.Select(a=>a with {Provenance=campaign.Provenance}).ToArray(),snapshot.Boms,[],[],[],new HashSet<string>(),new HashSet<string>(),ct);
  var json=JsonSerializer.Serialize(snapshot);
  var count=snapshot.Articles.Count+snapshot.Boms.Count;
  await repository.SaveImportBatchAsync(new Mvp0ImportBatchDto(0,id,"CONTEXT",$"CBN-{context.CbnRunId}-articles-bom.json",Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))),campaign.ImportVersion+1,count,count,0,"IMPORTED",campaign.Provenance,DateTime.UtcNow,JsonSerializer.Serialize(new {Articles=snapshot.Articles.Count,Boms=snapshot.Boms.Count,Missing="Gammes, calendriers, centres, liens BOM-opérations et historique OF non fournis"})),ct);
  await repository.UpdateCampaignStatusAsync(id,"VALIDATING",cancellationToken:ct);
 }
}
