using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Axioplan.GammesNomenclatures.Application.Abstractions;
using Axioplan.GammesNomenclatures.Application.Models;
using Axioplan.GammesNomenclatures.Domain.Mvp0;
using Microsoft.Extensions.DependencyInjection;

namespace Axioplan.GammesNomenclatures.Application.Mvp0;

public static class Mvp0Extensions
{
    public static IServiceCollection AddMvp0Application(this IServiceCollection services)
    {
        services.AddScoped<Mvp0WorkflowService>();
        services.AddScoped<Mvp0ContextService>();
        services.AddMvp0ApsGateGuard();
        services.AddMvp0ApsBridge();
        return services;
    }
}

public sealed class Mvp0WorkflowService(IMvp0Repository repository, Mvp0ApsBridgeService bridgeService, IMvp0ContextRepository? contextRepository = null)
{
    public static IReadOnlyList<string> Steps { get; } =
    [
        "Famille", "Imports", "Validation", "Corrections", "FiabiliteIntrant",
        "Backtest", "FiabiliteResultat", "AvisPlanificateur", "Gate", "Rapport"
    ];

    public Task EnsureReadyAsync(CancellationToken cancellationToken = default)
        => EnsureAllAsync(cancellationToken);

    private async Task EnsureAllAsync(CancellationToken cancellationToken)
    {
        await repository.EnsureSchemaAsync(cancellationToken);
        await repository.SeedDemoAsync(cancellationToken);
    }

    public Task<IReadOnlyList<Mvp0CampaignDto>> ListCampaignsAsync(CancellationToken cancellationToken = default)
        => repository.ListCampaignsAsync(cancellationToken);

    public async Task<Mvp0CampaignDto> CreateCampaignAsync(
        string familyCode,
        string site,
        string owner,
        DateOnly from,
        DateOnly to,
        bool useSimulatedDemo,
        CancellationToken cancellationToken = default)
    {
        await EnsureAllAsync(cancellationToken);
        var c = new Mvp0Campaign(
            Guid.NewGuid(),
            "MVP0-" + familyCode.ToUpperInvariant() + "-" + DateTime.UtcNow.ToString("yyyyMMddHHmm"),
            familyCode.Trim(),
            site,
            from,
            to,
            owner,
            Mvp0CampaignStatuses.Draft,
            useSimulatedDemo ? "DEMO_SEED" : "UPLOAD",
            useSimulatedDemo ? Mvp0Provenance.Simulated : Mvp0Provenance.Real,
            DateTime.UtcNow,
            0,
            85,
            70,
            null);
        var saved = await repository.CreateCampaignAsync(c, cancellationToken);
        return ToDto(saved);
    }

    public async Task<Mvp0WorkflowStateDto> GetStateAsync(Guid campaignId, CancellationToken cancellationToken = default)
    {
        await EnsureAllAsync(cancellationToken);
        var c = await repository.GetCampaignAsync(campaignId) ?? throw new InvalidOperationException("Campagne introuvable.");
        var anoms = await repository.ListAnomaliesAsync(campaignId, cancellationToken);
        var bps = await repository.ListBypassesAsync(campaignId, cancellationToken);
        var (input, result, bt, gate) = await repository.GetScoresAsync(campaignId, cancellationToken);
        var (pn, pc, pa) = await repository.GetPlannerReviewAsync(campaignId, cancellationToken);
        var report = await repository.GetReportAsync(campaignId, cancellationToken) ?? "";
        var completed = DeriveCompleted(c, anoms, bps, input, bt, result, pa, gate, report);
        var current = Steps.FirstOrDefault(s => !completed.Contains(s)) ?? "Rapport";
        return new Mvp0WorkflowStateDto(ToDto(c), completed, current, anoms, bps, input, bt, result, gate, pn, pc, pa, report) { Context = contextRepository is null ? null : await contextRepository.GetCampaignContextAsync(campaignId, cancellationToken) };
    }

    public Task<IReadOnlyList<Mvp0ImportBatchDto>> ListImportBatchesAsync(Guid campaignId, CancellationToken cancellationToken = default)
        => repository.ListImportBatchesAsync(campaignId, cancellationToken);

    public Mvp0CsvPreview PreviewCsv(string dataType, string csvContent)
        => Mvp0CsvImportEngine.Preview(dataType, csvContent);

    public async Task<IReadOnlyList<(string SheetName, string Csv)>> ConvertExcelAsync(
        byte[] fileContent,
        string? fileName = null,
        CancellationToken cancellationToken = default)
        => await repository.ConvertExcelToCsvSheetsAsync(fileContent, fileName, cancellationToken);

    public async Task<Mvp0ImportBatchDto> ImportCsvAsync(
        Guid campaignId,
        string dataType,
        string fileName,
        string csvContent,
        IReadOnlyDictionary<string, string>? mappingOverride,
        CancellationToken cancellationToken = default)
    {
        await EnsureAllAsync(cancellationToken);
        var camp = await repository.GetCampaignAsync(campaignId) ?? throw new InvalidOperationException("Campagne introuvable.");
        await repository.UpdateCampaignStatusAsync(campaignId, Mvp0CampaignStatuses.Importing, cancellationToken: cancellationToken);

        var (report, payload) = Mvp0CsvImportEngine.Parse(dataType, csvContent, mappingOverride, camp.Provenance);
        var (articles, boms, ops, cals, wos, centers, links) = await repository.LoadWorkingSetAsync(campaignId, cancellationToken);

        switch (dataType.ToUpperInvariant())
        {
            case Mvp0DataTypes.Articles when payload is List<Mvp0ArticleRow> a:
                articles = articles.Concat(a).ToList();
                break;
            case Mvp0DataTypes.Boms when payload is List<Mvp0BomRow> b:
                boms = boms.Concat(b).ToList();
                break;
            case Mvp0DataTypes.Routings when payload is List<Mvp0RoutingOpRow> r:
                ops = ops.Concat(r).ToList();
                break;
            case Mvp0DataTypes.Calendars when payload is List<Mvp0CalendarRow> c:
                cals = cals.Concat(c).ToList();
                break;
            case Mvp0DataTypes.WorkOrders when payload is List<Mvp0WorkOrderActual> w:
                wos = wos.Concat(w).ToList();
                break;
            case Mvp0DataTypes.Centers when payload is List<string> cc:
                centers = centers.Concat(cc).ToHashSet(StringComparer.OrdinalIgnoreCase);
                break;
            case Mvp0DataTypes.BomOpLinks when payload is List<string> ll:
                links = links.Concat(ll).ToHashSet(StringComparer.OrdinalIgnoreCase);
                break;
        }

        await repository.ReplaceWorkingSetAsync(campaignId, articles, boms, ops, cals, wos, centers, links, cancellationToken);

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(csvContent)));
        var version = camp.ImportVersion + 1;
        var batch = new Mvp0ImportBatchDto(
            0, campaignId, dataType, fileName, hash, version,
            report.LinesRead, report.LinesImported, report.LinesRejected, "COMPLETED", camp.Provenance, DateTime.UtcNow,
            JsonSerializer.Serialize(report));
        batch = await repository.SaveImportBatchAsync(batch, cancellationToken);
        await repository.SaveImportMappingAsync(campaignId, batch.Id, dataType, JsonSerializer.Serialize(report.MappingUsed), cancellationToken);
        await repository.UpdateCampaignStatusAsync(campaignId, Mvp0CampaignStatuses.Validating, cancellationToken: cancellationToken);
        return batch;
    }

    /// <summary>Jeu REAL cohérent (sans anomalies bloquantes volontaires) pour illustrer GO / GO_WITH_RESERVATIONS.</summary>
    public async Task<Mvp0ImportBatchDto> ImportCleanRealSeedAsync(Guid campaignId, CancellationToken cancellationToken = default)
    {
        await EnsureAllAsync(cancellationToken);
        if (contextRepository is not null && await contextRepository.GetCampaignContextAsync(campaignId, cancellationToken) is not null)
            throw new InvalidOperationException("Les seeds autonomes ne remplacent pas le contexte lié. Importer les données de qualité de la campagne.");
        var camp = await repository.GetCampaignAsync(campaignId) ?? throw new InvalidOperationException("Campagne introuvable.");
        if (camp.Provenance == Mvp0Provenance.Simulated)
            throw new InvalidOperationException("Import clean REAL interdit sur campagne SIMULATED — créer une campagne REAL.");

        var family = camp.FamilyCode;
        var articles = new List<Mvp0ArticleRow>
        {
            new($"{family}-PF-01", "FINISHED", "UN", family, true, Mvp0Provenance.Real, 2),
            new($"{family}-SF-01", "SEMI", "UN", family, true, Mvp0Provenance.Real, 3),
            new($"{family}-RM-01", "COMPONENT", "KG", family, true, Mvp0Provenance.Real, 4)
        };
        var boms = new List<Mvp0BomRow>
        {
            new($"{family}-PF-01", $"{family}-SF-01", 1, "UN", 0.02, 2),
            new($"{family}-SF-01", $"{family}-RM-01", 0.2, "KG", 0.01, 3)
        };
        var ops = new List<Mvp0RoutingOpRow>
        {
            new($"{family}-PF-01", "REMAIL", 10, "CC_REMAIL", 0.2, 0.1, 2),
            new($"{family}-SF-01", "TRICOT", 10, "CC_TRICOT", 0.15, 0.05, 3)
        };
        var cals = new List<Mvp0CalendarRow>
        {
            new("CAL_JOUR", new TimeOnly(6, 0), new TimeOnly(14, 0), "EQ1", 0.85, 2),
            new("CAL_APREM", new TimeOnly(14, 0), new TimeOnly(22, 0), "EQ2", 0.8, 3)
        };
        var centers = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "CC_REMAIL", "CC_TRICOT" };
        var links = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            $"{family}-PF-01|{family}-SF-01",
            $"{family}-SF-01|{family}-RM-01"
        };
        var today = DateOnly.FromDateTime(DateTime.Today);
        var wos = new List<Mvp0WorkOrderActual>
        {
            new("OF-200", $"{family}-PF-01", today.AddDays(-20), today.AddDays(-19), 100, 98, 2, 20, 21, Mvp0Provenance.Real),
            new("OF-201", $"{family}-PF-01", today.AddDays(-15), today.AddDays(-15), 80, 80, 0, 16, 16.5, Mvp0Provenance.Real),
            new("OF-202", $"{family}-SF-01", today.AddDays(-12), today.AddDays(-11), 50, 49, 1, 10, 10.2, Mvp0Provenance.Real)
        };

        await repository.ReplaceWorkingSetAsync(campaignId, articles, boms, ops, cals, wos, centers, links, cancellationToken);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("clean-real-" + camp.Code)));
        var batch = new Mvp0ImportBatchDto(
            0, campaignId, "FULL_CLEAN_REAL", "mvp0-clean-real.csv", hash, camp.ImportVersion + 1,
            12, articles.Count + boms.Count + ops.Count + cals.Count + wos.Count, 0, "COMPLETED", Mvp0Provenance.Real, DateTime.UtcNow,
            JsonSerializer.Serialize(new { assumptions = new[] { "Jeu cohérent REAL — TO_CONFIRM volumes métier" }, provenance = Mvp0Provenance.Real }));
        batch = await repository.SaveImportBatchAsync(batch, cancellationToken);
        await repository.UpdateCampaignStatusAsync(campaignId, Mvp0CampaignStatuses.Validating, cancellationToken: cancellationToken);
        return batch;
    }

    public async Task<Mvp0ImportBatchDto> ImportCsvDemoSeedAsync(Guid campaignId, CancellationToken cancellationToken = default)
    {
        await EnsureAllAsync(cancellationToken);
        if (contextRepository is not null && await contextRepository.GetCampaignContextAsync(campaignId, cancellationToken) is not null)
            throw new InvalidOperationException("Les seeds autonomes ne remplacent pas le contexte lié. Importer les données de qualité de la campagne.");
        var camp = await repository.GetCampaignAsync(campaignId) ?? throw new InvalidOperationException("Campagne introuvable.");
        await repository.UpdateCampaignStatusAsync(campaignId, Mvp0CampaignStatuses.Importing, cancellationToken: cancellationToken);

        var family = camp.FamilyCode;
        var prov = camp.Provenance;
        var articles = new List<Mvp0ArticleRow>
        {
            new($"{family}-PF-01", "FINISHED", "UN", family, true, prov, 2),
            new($"{family}-PF-01", "FINISHED", "UN", family, true, prov, 3), // dup volontaire
            new($"{family}-SF-01", "SEMI", "UN", family, true, prov, 4),
            new($"{family}-RM-01", "COMPONENT", "KG", family, true, prov, 5),
            new($"{family}-RM-02", "COMPONENT", null, family, true, prov, 6), // unit missing
            new("ORPHAN-X", "FINISHED", "UN", "OTHER", true, prov, 7)
        };
        // Reject orphan via validation later; also parse reject simulation
        var parseRejects = new List<string> { "Ligne 1: en-tête ignorée documentée", "Ligne 8: ligne inconnue rejetée (format invalide)" };

        var boms = new List<Mvp0BomRow>
        {
            new($"{family}-PF-01", $"{family}-SF-01", 1, "UN", 0.02, 2),
            new($"{family}-SF-01", $"{family}-RM-01", 0.2, "KG", 0.01, 3),
            new($"{family}-RM-01", $"{family}-SF-01", 1, "KG", 0.01, 4), // cycle
            new($"{family}-PF-01", $"{family}-SF-01", 1, "UN", 0.02, 5) // dup
        };

        var ops = new List<Mvp0RoutingOpRow>
        {
            new($"{family}-PF-01", "REMAIL", 10, "CC_REMAIL", 0.2, 0.1, 2),
            new($"{family}-PF-01", "CTR", 20, "CC_UNKNOWN", 0.05, 0, 3),
            new($"{family}-SF-01", "TRICOT", 10, "CC_TRICOT", -1, 0, 4), // negative
            new($"{family}-SF-01", "TRICOT2", 20, "CC_TRICOT", 50, 0, 5) // out of range warning
        };

        var cals = new List<Mvp0CalendarRow>
        {
            new("CAL_JOUR", new TimeOnly(6, 0), new TimeOnly(14, 0), "EQ1", 0.85, 2),
            new("CAL_JOUR", new TimeOnly(13, 0), new TimeOnly(21, 0), "EQ2", 0.8, 3), // overlap
            new("CAL_NUIT", new TimeOnly(22, 0), new TimeOnly(21, 0), "EQ3", 1.2, 4) // bad range + trs
        };

        var centers = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "CC_REMAIL", "CC_TRICOT" };
        var links = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            $"{family}-PF-01|{family}-SF-01",
            $"{family}-SF-01|{family}-RM-01"
            // cycle link missing intentionally for BOM_OP on cycle line optional
        };

        var today = DateOnly.FromDateTime(DateTime.Today);
        var wos = new List<Mvp0WorkOrderActual>
        {
            new("OF-100", $"{family}-PF-01", today.AddDays(-20), today.AddDays(-18), 100, 96, 4, 20, 22, prov),
            new("OF-101", $"{family}-PF-01", today.AddDays(-15), today.AddDays(-10), 80, 78, 2, 16, 19, prov),
            new("OF-102", $"{family}-SF-01", today.AddDays(-12), today.AddDays(-12), 50, 50, 0, 10, 9.5, prov)
        };

        await repository.ReplaceWorkingSetAsync(campaignId, articles, boms, ops, cals, wos, centers, links, cancellationToken);

        var csv = "demo-seed.csv";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(csv + camp.Code + camp.ImportVersion)));
        var report = new
        {
            linesRead = 8,
            linesImported = articles.Count + boms.Count + ops.Count + cals.Count + wos.Count,
            linesRejected = parseRejects.Count,
            rejects = parseRejects,
            duplicates = new[] { $"{family}-PF-01" },
            missingFields = new[] { "unit on RM-02" },
            warnings = Array.Empty<string>(),
            assumptions = new[] { "TRS convention [0;1] TO_CONFIRM" },
            provenance = prov
        };

        var batch = new Mvp0ImportBatchDto(
            0, campaignId, "FULL_SEED", "mvp0-demo-seed.csv", hash, camp.ImportVersion + 1,
            8, report.linesImported, parseRejects.Count, "COMPLETED", prov, DateTime.UtcNow,
            JsonSerializer.Serialize(report));
        batch = await repository.SaveImportBatchAsync(batch, cancellationToken);
        await repository.UpdateCampaignStatusAsync(campaignId, Mvp0CampaignStatuses.Validating, cancellationToken: cancellationToken);
        return batch;
    }

    public async Task<IReadOnlyList<Mvp0Anomaly>> RunValidationAsync(Guid campaignId, CancellationToken cancellationToken = default)
    {
        await EnsureAllAsync(cancellationToken);
        var camp = await repository.GetCampaignAsync(campaignId) ?? throw new InvalidOperationException("Campagne introuvable.");
        var (articles, boms, ops, cals, wos, centers, links) = await repository.LoadWorkingSetAsync(campaignId, cancellationToken);
        var (_, _, minCycle, maxCycle, _, _, _, _) = await repository.GetThresholdsAsync(cancellationToken);

        var articleSet = articles.Select(a => a.Code).Where(c => !string.IsNullOrWhiteSpace(c)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var anoms = new List<Mvp0Anomaly>();
        anoms.AddRange(Mvp0ValidationEngine.ValidateArticles(articles, camp.FamilyCode));
        anoms.AddRange(Mvp0ValidationEngine.ValidateBoms(boms, articleSet, requireOpLink: true, links));
        anoms.AddRange(Mvp0ValidationEngine.ValidateRoutings(ops, articleSet, centers, minCycle, maxCycle));
        anoms.AddRange(Mvp0ValidationEngine.ValidateCalendars(cals, trsZeroToOne: true));
        if (wos.Count == 0)
            anoms.Add(new Mvp0Anomaly("WO_EMPTY", "WO_HISTORY_EMPTY", Mvp0Severities.Blocking, "WO", camp.FamilyCode, "0", ">0", "MVP0", null, "Historique OF vide", "Importer OF", "OPEN", DateTime.UtcNow));

        await repository.SaveAnomaliesAsync(campaignId, anoms, cancellationToken);
        var openBlock = anoms.Count(a => a.Severity == Mvp0Severities.Blocking);
        await repository.UpdateCampaignStatusAsync(campaignId,
            openBlock > 0 ? Mvp0CampaignStatuses.NeedsCorrection : Mvp0CampaignStatuses.ReadyForBacktest,
            cancellationToken: cancellationToken);
        return anoms;
    }

    public async Task<Mvp0Bypass> CreateBypassAsync(
        Guid campaignId,
        string anomalyId,
        string justification,
        string decider,
        CancellationToken cancellationToken = default)
    {
        var anoms = await repository.ListAnomaliesAsync(campaignId, cancellationToken);
        var a = anoms.FirstOrDefault(x => x.AnomalyId == anomalyId)
                ?? throw new InvalidOperationException("Anomalie introuvable.");
        var bp = Mvp0BypassRules.Create(a.AnomalyId, a.ObjectKey, justification, decider, "MEDIUM", 3);
        await repository.SaveBypassAsync(campaignId, bp, cancellationToken);
        return bp;
    }

    public async Task<Mvp0InputReliabilityResult> ComputeInputReliabilityAsync(Guid campaignId, CancellationToken cancellationToken = default)
    {
        var anoms = await repository.ListAnomaliesAsync(campaignId, cancellationToken);
        var bps = (await repository.ListBypassesAsync(campaignId, cancellationToken)).Where(b => b.Status == "ACTIVE").ToList();
        var weights = await repository.GetWeightsAsync(cancellationToken);
        var (_, _, _, _, aging, stale, _, _) = await repository.GetThresholdsAsync(cancellationToken);
        var checks = new Dictionary<string, int>
        {
            ["Articles"] = 20, ["Nomenclatures"] = 15, ["Gammes"] = 15, ["Temps"] = 10,
            ["Calendriers"] = 8, ["TRS"] = 5, ["LiaisonBOM-OP"] = 8, ["HistoriqueOF"] = 10
        };
        var result = Mvp0DataReliabilityEngine.Compute(anoms, bps, weights, checks, DateTime.UtcNow.AddDays(-3), DateTime.UtcNow, aging, stale);
        var (_, res, _, _) = await repository.GetScoresAsync(campaignId, cancellationToken);
        await repository.SaveReliabilityAsync(campaignId, result, res, cancellationToken);
        return result;
    }

    public async Task<(Mvp0BacktestResult Backtest, Mvp0ResultReliabilityResult Score)> RunBacktestAsync(
        Guid campaignId,
        CancellationToken cancellationToken = default)
    {
        var anoms = await repository.ListAnomaliesAsync(campaignId, cancellationToken);
        var bps = await repository.ListBypassesAsync(campaignId, cancellationToken);
        var activeBp = bps.Where(b => b.Status == "ACTIVE").Select(b => b.AnomalyId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var openBlock = anoms.Any(a => a.Severity == Mvp0Severities.Blocking && a.Status == "OPEN" && !activeBp.Contains(a.AnomalyId));
        if (openBlock)
            throw new InvalidOperationException("Backtest interdit : anomalies BLOCKING non by-passées (ex. cycle BOM).");

        var (_, _, _, _, wos, _, _) = await repository.LoadWorkingSetAsync(campaignId, cancellationToken);
        var (_, _, _, _, _, _, dateTol, durTol) = await repository.GetThresholdsAsync(cancellationToken);
        var bt = Mvp0BacktestReliabilityEngine.Run(wos, dateTol, durTol);
        var score = Mvp0BacktestReliabilityEngine.Score(bt);
        await repository.SaveBacktestAsync(campaignId, bt, cancellationToken);
        var (input, _, _, _) = await repository.GetScoresAsync(campaignId, cancellationToken);
        if (input is not null)
            await repository.SaveReliabilityAsync(campaignId, input, score, cancellationToken);
        await repository.UpdateCampaignStatusAsync(campaignId, Mvp0CampaignStatuses.Backtested, cancellationToken: cancellationToken);
        return (bt, score);
    }

    public async Task SavePlannerReviewAsync(
        Guid campaignId,
        string name,
        string comment,
        bool approved,
        CancellationToken cancellationToken = default)
    {
        var (input, result, _, _) = await repository.GetScoresAsync(campaignId, cancellationToken);
        var gatePlaceholder = new Mvp0GateDecision(Mvp0GateOutcomes.InsufficientData, "pending", true, approved,
            input?.GlobalScore ?? 0, result?.GlobalScore ?? 0, 0, 0, false);
        await repository.SaveGateAsync(campaignId, gatePlaceholder, name, comment, approved, cancellationToken);
    }

    public async Task<Mvp0GateDecision> RunGateAsync(Guid campaignId, CancellationToken cancellationToken = default)
    {
        var camp = await repository.GetCampaignAsync(campaignId) ?? throw new InvalidOperationException("Campagne introuvable.");
        var (input, result, _, _) = await repository.GetScoresAsync(campaignId, cancellationToken);
        if (input is null || result is null)
            throw new InvalidOperationException("Calculer d'abord les scores intrant et résultat.");

        var anoms = await repository.ListAnomaliesAsync(campaignId, cancellationToken);
        var bps = (await repository.ListBypassesAsync(campaignId, cancellationToken)).Where(b => b.Status == "ACTIVE").ToList();
        var (pn, pc, approved) = await repository.GetPlannerReviewAsync(campaignId, cancellationToken);
        var (go, goRes, _, _, _, _, _, _) = await repository.GetThresholdsAsync(cancellationToken);
        var anySim = camp.Provenance == Mvp0Provenance.Simulated
                     || (await repository.ListImportBatchesAsync(campaignId, cancellationToken)).Any(b => b.Provenance == Mvp0Provenance.Simulated);

        var gate = Mvp0GateDecisionEngine.Decide(input, result, anoms, bps, approved, anySim, go, goRes);
        await repository.SaveGateAsync(campaignId, gate, pn ?? "", pc ?? "", approved, cancellationToken);
        var status = gate.Outcome switch
        {
            Mvp0GateOutcomes.Go => Mvp0CampaignStatuses.Go,
            Mvp0GateOutcomes.GoWithReservations => Mvp0CampaignStatuses.Go,
            Mvp0GateOutcomes.NoGo => Mvp0CampaignStatuses.NoGo,
            _ => Mvp0CampaignStatuses.Backtested
        };
        await repository.UpdateCampaignStatusAsync(campaignId, status, gate.Outcome, cancellationToken);
        await bridgeService.PromoteIfApprovedAsync(campaignId, gate, cancellationToken);
        return gate;
    }

    public async Task<string> BuildReportAsync(Guid campaignId, CancellationToken cancellationToken = default)
    {
        var state = await GetStateAsync(campaignId, cancellationToken);
        var c = state.Campaign;
        var domainRows = string.Join("", (state.InputReliability?.Domains ?? []).Select(d =>
            "<tr><td>" + d.Domain + "</td><td>" + d.Score.ToString("F0", CultureInfo.InvariantCulture)
            + "</td><td>" + System.Net.WebUtility.HtmlEncode(d.Formula) + "</td></tr>"));
        var blockingLis = string.Join("", state.Anomalies.Where(a => a.Severity == Mvp0Severities.Blocking).Take(8)
            .Select(a => "<li>" + System.Net.WebUtility.HtmlEncode(a.RuleCode + " — " + a.ObjectKey + ": " + a.Message) + "</li>"));
        var plannerLabel = state.PlannerApproved ? "approuvé" : "non";
        var gateOutcome = state.Gate?.Outcome ?? c.GateOutcome ?? "—";
        var inputScore = state.InputReliability?.GlobalScore.ToString("F1", CultureInfo.InvariantCulture) ?? "—";
        var resultScore = state.ResultReliability?.GlobalScore.ToString("F1", CultureInfo.InvariantCulture) ?? "—";
        var openBlocking = state.Anomalies.Count(a => a.Severity == Mvp0Severities.Blocking && a.Status == "OPEN");
        var activeBp = state.Bypasses.Count(b => b.Status == "ACTIVE");
        var meanDelay = state.Backtest?.MeanDateErrorDays.ToString("F1", CultureInfo.InvariantCulture) ?? "—";
        var p90 = state.Backtest?.P90DateErrorDays.ToString("F1", CultureInfo.InvariantCulture) ?? "—";
        var mape = state.Backtest?.MapeDuration.ToString("F1", CultureInfo.InvariantCulture) ?? "—";

        var contextHtml = state.Context is { } ctx ? "<div class=\"box\"><b>Contexte:</b> " + System.Net.WebUtility.HtmlEncode(ctx.OrderCode + " / " + ctx.FamilyCode) + $" — CBN #{ctx.CbnRunId}, pegging #{ctx.PeggingRunId} v{ctx.PeggingVersion}. Jeu de qualité indépendant des allocations.</div>" : "";
        var html = new StringBuilder()
            .Append("<html><head><meta charset=\"utf-8\"><title>Gate 0→1 — ").Append(System.Net.WebUtility.HtmlEncode(c.Code)).Append("</title>")
            .Append("<style>body{font-family:Segoe UI,Arial,sans-serif;font-size:12px;margin:24px} h1{font-size:18px}")
            .Append(" table{border-collapse:collapse;width:100%} td,th{border:1px solid #ccc;padding:4px}")
            .Append(" .box{border:1px solid #333;padding:8px;margin:8px 0}</style></head><body>")
            .Append("<h1>Rapport GO / NO-GO — MVP-0 (1 page)</h1><div class=\"box\">")
            .Append("<b>Famille:</b> ").Append(System.Net.WebUtility.HtmlEncode(c.FamilyCode))
            .Append(" &nbsp; <b>Site:</b> ").Append(System.Net.WebUtility.HtmlEncode(c.SiteCode))
            .Append(" &nbsp; <b>Période:</b> ").Append(c.PeriodFrom).Append(" → ").Append(c.PeriodTo).Append("<br/>")
            .Append("<b>Provenance:</b> ").Append(c.Provenance)
            .Append(" &nbsp; <b>Version import:</b> ").Append(c.ImportVersion)
            .Append(" &nbsp; <b>Statut:</b> ").Append(c.Status).Append("</div>")
            .Append(contextHtml)
            .Append("<table><tr><th>Score intrant</th><th>Score résultat</th><th>Gate</th><th>Planificateur</th></tr><tr>")
            .Append("<td>").Append(inputScore).Append("</td><td>").Append(resultScore).Append("</td>")
            .Append("<td><b>").Append(System.Net.WebUtility.HtmlEncode(gateOutcome)).Append("</b></td>")
            .Append("<td>").Append(System.Net.WebUtility.HtmlEncode(state.PlannerName ?? "")).Append(" — ").Append(plannerLabel).Append("</td></tr></table>")
            .Append("<h3>Scores par domaine (intrant)</h3><table><tr><th>Domaine</th><th>Score</th><th>Formule</th></tr>")
            .Append(domainRows).Append("</table>")
            .Append("<h3>Anomalies critiques / by-pass</h3><p>BLOCKING ouverts: ").Append(openBlocking)
            .Append(" — By-pass actifs: ").Append(activeBp).Append("</p><ul>").Append(blockingLis).Append("</ul>")
            .Append("<h3>Principaux écarts backtest</h3><p>OF=").Append(state.Backtest?.WoCount)
            .Append(" retard moy=").Append(meanDelay).Append("j P90=").Append(p90)
            .Append(" MAPE durée=").Append(mape).Append("%</p>")
            .Append("<h3>Avis planificateur</h3><p>")
            .Append(System.Net.WebUtility.HtmlEncode(state.PlannerComment ?? "—")).Append("</p>")
            .Append("<h3>Décision &amp; conditions MVP-1</h3><p><b>")
            .Append(System.Net.WebUtility.HtmlEncode(gateOutcome)).Append("</b> — ")
            .Append(System.Net.WebUtility.HtmlEncode(state.Gate?.Reason ?? "")).Append("</p>")
            .Append("<p>Conditions: lever by-pass critiques, remplacer données SIMULATED par REAL, atteindre seuils GO configurés.</p>")
            .Append("<p><small>Généré ").Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture))
            .Append(" — ").Append(System.Net.WebUtility.HtmlEncode(c.Code)).Append("</small></p></body></html>")
            .ToString();
        await repository.SaveReportAsync(campaignId, html, cancellationToken);
        return html;
    }

    /// <summary>Enchaîne le parcours demo de bout en bout (avec by-pass des bloquants).</summary>
    public async Task<Mvp0WorkflowStateDto> RunDemoEndToEndAsync(
        string family,
        string owner,
        bool simulated,
        CancellationToken cancellationToken = default)
    {
        var camp = await CreateCampaignAsync(family, "SITE-1", owner, DateOnly.FromDateTime(DateTime.Today.AddDays(-60)), DateOnly.FromDateTime(DateTime.Today), simulated, cancellationToken);
        await ImportCsvDemoSeedAsync(camp.Id, cancellationToken);
        var anoms = await RunValidationAsync(camp.Id, cancellationToken);
        foreach (var a in anoms.Where(x => x.Severity == Mvp0Severities.Blocking))
        {
            try
            {
                await CreateBypassAsync(camp.Id, a.AnomalyId, "By-pass demo tracé pour poursuite test MVP-0", owner, cancellationToken);
            }
            catch { /* duplicate anomaly ids possible */ }
        }

        await ComputeInputReliabilityAsync(camp.Id, cancellationToken);
        await RunBacktestAsync(camp.Id, cancellationToken);
        await SavePlannerReviewAsync(camp.Id, owner, "Validation planificateur demo MVP-0", approved: true, cancellationToken);
        await RunGateAsync(camp.Id, cancellationToken);
        await BuildReportAsync(camp.Id, cancellationToken);
        return await GetStateAsync(camp.Id, cancellationToken);
    }

    private static List<string> DeriveCompleted(
        Mvp0Campaign c,
        IReadOnlyList<Mvp0Anomaly> anoms,
        IReadOnlyList<Mvp0Bypass> bps,
        Mvp0InputReliabilityResult? input,
        Mvp0BacktestResult? bt,
        Mvp0ResultReliabilityResult? result,
        bool planner,
        Mvp0GateDecision? gate,
        string report)
    {
        var done = new List<string> { "Famille" };
        if (c.ImportVersion > 0 || c.Status != Mvp0CampaignStatuses.Draft) done.Add("Imports");
        if (anoms.Count > 0) done.Add("Validation");
        if (bps.Count > 0 || c.Status is Mvp0CampaignStatuses.ReadyForBacktest or Mvp0CampaignStatuses.Backtested or Mvp0CampaignStatuses.Go or Mvp0CampaignStatuses.NoGo)
            done.Add("Corrections");
        if (input is not null) done.Add("FiabiliteIntrant");
        if (bt is not null) done.Add("Backtest");
        if (result is not null) done.Add("FiabiliteResultat");
        if (planner) done.Add("AvisPlanificateur");
        if (gate is not null && gate.Outcome is not (Mvp0GateOutcomes.InsufficientData) || c.GateOutcome is not null)
        {
            // still mark Gate if decision recorded and planner done
            if (c.GateOutcome is not null) done.Add("Gate");
        }

        if (gate is not null && planner) done.Add("Gate");
        if (!string.IsNullOrWhiteSpace(report)) done.Add("Rapport");
        return done.Distinct().ToList();
    }

    private static Mvp0CampaignDto ToDto(Mvp0Campaign c)
        => new(c.Id, c.Code, c.FamilyCode, c.SiteCode, c.PeriodFrom, c.PeriodTo, c.Owner, c.Status, c.Provenance, c.ImportVersion, c.GateOutcome, c.CreatedAtUtc);
}
