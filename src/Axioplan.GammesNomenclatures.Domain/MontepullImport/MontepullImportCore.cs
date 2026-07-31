using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Axioplan.GammesNomenclatures.Domain.MontepullImport;

public enum MontepullFileKind
{
    Unknown,
    Commandes,
    ListeSuivi,
    SuiviOperations,
    Doublygilf,
    NomenclatureDoulbygilf
}

public static class MontepullFileDetector
{
    public static MontepullFileKind Detect(string fileName, IReadOnlyList<string> sheetNames, IReadOnlyList<string>? firstHeaders = null)
    {
        var name = Normalize(fileName);
        if (name.Contains("commande", StringComparison.Ordinal))
            return MontepullFileKind.Commandes;
        if (name.Contains("listesuivi", StringComparison.Ordinal) || name.Contains("liste_suivi", StringComparison.Ordinal))
            return MontepullFileKind.ListeSuivi;
        if (name.Contains("suivioperation", StringComparison.Ordinal) || name.Contains("suivi_operation", StringComparison.Ordinal))
            return MontepullFileKind.SuiviOperations;
        if (name.Contains("nomenclature", StringComparison.Ordinal) && (name.Contains("doul", StringComparison.Ordinal) || name.Contains("doub", StringComparison.Ordinal)))
            return MontepullFileKind.NomenclatureDoulbygilf;
        if (name.Contains("doublygilf", StringComparison.Ordinal) || name.Contains("doulbygilf", StringComparison.Ordinal))
            return MontepullFileKind.Doublygilf;

        var sheets = sheetNames.Select(Normalize).ToList();
        if (sheets.Any(s => s.Contains("commande", StringComparison.Ordinal)))
            return MontepullFileKind.Commandes;
        if (sheets.Any(s => s.Contains("feuille route", StringComparison.Ordinal) || s.Contains("par operation", StringComparison.Ordinal)))
            return MontepullFileKind.ListeSuivi;
        if (sheets.Count >= 1 && sheets.Any(s => s.Contains("par of", StringComparison.Ordinal)))
            return MontepullFileKind.SuiviOperations;

        if (firstHeaders is { Count: > 0 })
        {
            var h = string.Join('|', firstHeaders.Select(Normalize));
            if (h.Contains("qte_commandee", StringComparison.Ordinal) || h.Contains("qte_lancee", StringComparison.Ordinal))
                return MontepullFileKind.Commandes;
            if (h.Contains("npaquet", StringComparison.Ordinal) || h.Contains("qte of", StringComparison.Ordinal))
                return MontepullFileKind.ListeSuivi;
            if (h.Contains("operation", StringComparison.Ordinal) && h.Contains("n of", StringComparison.Ordinal))
                return MontepullFileKind.SuiviOperations;
        }

        return MontepullFileKind.Unknown;
    }

    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var formD = value.Trim().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(formD.Length);
        foreach (var ch in formD)
        {
            var cat = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (cat != UnicodeCategory.NonSpacingMark)
                sb.Append(ch);
        }

        var cleaned = sb.ToString().Normalize(NormalizationForm.FormC).ToLowerInvariant();
        cleaned = Regex.Replace(cleaned, @"[^a-z0-9]+", " ").Trim();
        return cleaned;
    }

    public static string NormalizeHeader(string? header) => Normalize(header).Replace(' ', '_');
}

public static class MontepullQuantityRules
{
    public static double RemainingToLaunch(double ordered, double launched)
        => Math.Max(ordered - launched, 0);

    public static double RemainingToProduce(double ordered, double produced)
        => Math.Max(ordered - produced, 0);

    public static string LaunchStatus(double ordered, double launched, double produced)
    {
        if (ordered <= 0)
            return "EMPTY";
        if (launched <= 0)
            return "NOT_LAUNCHED";
        if (RemainingToLaunch(ordered, launched) > 0)
            return "PARTIALLY_LAUNCHED";
        if (RemainingToProduce(ordered, produced) <= 0)
            return "COMPLETED";
        return "FULLY_LAUNCHED";
    }
}

public static class MontepullDurationAnalyzer
{
    public const double ScanThresholdSeconds = 30;
    public const double AberrantThresholdSeconds = 7 * 24 * 3600; // 7 days

    public static (double? Seconds, string Kind, string? AnomalyCode) Analyze(DateTime? start, DateTime? end)
    {
        if (start is null || end is null)
            return (null, "UNKNOWN", null);
        if (end < start)
            return (null, "INVALID", "DATE_END_BEFORE_START");

        var seconds = (end.Value - start.Value).TotalSeconds;
        if (seconds < 0)
            return (null, "INVALID", "NEGATIVE_DURATION");
        if (seconds > AberrantThresholdSeconds)
            return (seconds, "INVALID", "ABERRANT_DURATION");
        if (seconds < ScanThresholdSeconds)
            return (seconds, "SCAN", null);
        return (seconds, "PROCESS", null);
    }

    public static MontepullDurationStats ComputeStats(IReadOnlyList<double> values, int excludedAnomalies)
    {
        if (values.Count == 0)
        {
            return new MontepullDurationStats(0, null, null, null, null, null, null, null, excludedAnomalies);
        }

        var sorted = values.OrderBy(v => v).ToList();
        var mean = sorted.Average();
        var median = Percentile(sorted, 0.5);
        var p25 = Percentile(sorted, 0.25);
        var p75 = Percentile(sorted, 0.75);
        var variance = sorted.Sum(v => Math.Pow(v - mean, 2)) / sorted.Count;
        return new MontepullDurationStats(
            sorted.Count,
            mean,
            median,
            sorted[0],
            sorted[^1],
            p25,
            p75,
            Math.Sqrt(variance),
            excludedAnomalies);
    }

    public static double Percentile(IReadOnlyList<double> sortedAscending, double p)
    {
        if (sortedAscending.Count == 0)
            return 0;
        if (sortedAscending.Count == 1)
            return sortedAscending[0];

        var pos = p * (sortedAscending.Count - 1);
        var lo = (int)Math.Floor(pos);
        var hi = (int)Math.Ceiling(pos);
        if (lo == hi)
            return sortedAscending[lo];
        var w = pos - lo;
        return sortedAscending[lo] * (1 - w) + sortedAscending[hi] * w;
    }
}

public sealed record MontepullDurationStats(
    int ObservationCount,
    double? MeanSeconds,
    double? MedianSeconds,
    double? MinSeconds,
    double? MaxSeconds,
    double? P25Seconds,
    double? P75Seconds,
    double? StdDevSeconds,
    int ExcludedAnomalyCount);

public sealed record MontepullOfOperationAggregate(
    string OfCode,
    int OperationNo,
    double QtyGoodTotal,
    double QtyRejectedTotal,
    DateTime? FirstStart,
    DateTime? LastEnd,
    int PacketCount,
    string? StatusAgg,
    double? RemainingQty);

public static class MontepullAggregation
{
    public static IReadOnlyList<MontepullOfOperationAggregate> AggregateByOfOperation(
        IEnumerable<(string OfCode, int Op, double Good, double Rejected, DateTime? Start, DateTime? End, string? Packet, string? Status, double? OfQty)> rows)
    {
        return rows
            .GroupBy(r => (r.OfCode, r.Op))
            .Select(g =>
            {
                var good = g.Sum(x => x.Good);
                var rejected = g.Sum(x => x.Rejected);
                var packets = g.Select(x => x.Packet ?? string.Empty).Where(p => p.Length > 0).Distinct().Count();
                var ofQty = g.Select(x => x.OfQty).FirstOrDefault(q => q is > 0);
                var remaining = ofQty is null ? (double?)null : Math.Max(ofQty.Value - good, 0);
                return new MontepullOfOperationAggregate(
                    g.Key.OfCode,
                    g.Key.Op,
                    good,
                    rejected,
                    g.Min(x => x.Start),
                    g.Max(x => x.End),
                    packets,
                    g.Select(x => x.Status).LastOrDefault(s => !string.IsNullOrWhiteSpace(s)),
                    remaining);
            })
            .OrderBy(a => a.OfCode)
            .ThenBy(a => a.OperationNo)
            .ToList();
    }
}

public static class MontepullHash
{
    public static string Sha256Hex(string content)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(bytes);
    }

    public static string Sha256Hex(byte[] content)
    {
        var bytes = SHA256.HashData(content);
        return Convert.ToHexString(bytes);
    }

    public static string RowHash(params string?[] parts)
        => Sha256Hex(string.Join('|', parts.Select(p => (p ?? string.Empty).Trim())));
}

public static class MontepullColumnIndex
{
    public static Dictionary<string, int> MapHeaders(IReadOnlyList<string> headers)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < headers.Count; i++)
        {
            var key = MontepullFileDetector.NormalizeHeader(headers[i]);
            if (string.IsNullOrWhiteSpace(key))
                continue;
            map.TryAdd(key, i);
        }

        return map;
    }

    public static string? Get(IReadOnlyList<string> row, Dictionary<string, int> map, params string[] keys)
    {
        foreach (var key in keys)
        {
            var nk = MontepullFileDetector.NormalizeHeader(key);
            if (map.TryGetValue(nk, out var idx) && idx < row.Count)
                return row[idx];
        }

        return null;
    }

    public static bool TryGetDouble(IReadOnlyList<string> row, Dictionary<string, int> map, out double value, params string[] keys)
    {
        value = 0;
        var raw = Get(row, map, keys);
        if (string.IsNullOrWhiteSpace(raw))
            return false;
        var cleaned = raw.Trim().Replace(" ", string.Empty, StringComparison.Ordinal).Replace(",", ".", StringComparison.Ordinal);
        return double.TryParse(cleaned, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    public static bool TryGetInt(IReadOnlyList<string> row, Dictionary<string, int> map, out int value, params string[] keys)
    {
        value = 0;
        if (!TryGetDouble(row, map, out var d, keys))
            return false;
        value = (int)Math.Round(d);
        return true;
    }

    public static DateTime? GetDateTime(IReadOnlyList<string> row, Dictionary<string, int> map, params string[] keys)
    {
        var raw = Get(row, map, keys);
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var dt))
            return dt;
        if (DateTime.TryParse(raw, new CultureInfo("fr-FR"), DateTimeStyles.AssumeLocal, out dt))
            return dt;
        if (double.TryParse(raw.Replace(",", ".", StringComparison.Ordinal), NumberStyles.Float, CultureInfo.InvariantCulture, out var oa))
        {
            try { return DateTime.FromOADate(oa); }
            catch { /* ignore */ }
        }

        return null;
    }

    public static DateOnly? GetDate(IReadOnlyList<string> row, Dictionary<string, int> map, params string[] keys)
    {
        var dt = GetDateTime(row, map, keys);
        return dt is null ? null : DateOnly.FromDateTime(dt.Value);
    }
}

public sealed record ParsedCommandeRow(
    int SourceRowNo,
    string OrderCode,
    string? CustomerCode,
    string ArticleCode,
    string? Designation,
    double QtyOrdered,
    double QtyLaunched,
    double? SommeOperations,
    double? SommeOp70Commande,
    double? SommeOp70Article,
    int? LastOperation,
    double QtyProduced,
    double RemainingToLaunch,
    double RemainingToProduce,
    string LaunchStatus,
    string RowHash);

public sealed record ParsedSuiviRow(
    int SourceRowNo,
    string SheetName,
    string SourceKind,
    string OfCode,
    string? OrderCode,
    string? ArticleCode,
    string? Designation,
    int? OperationNo,
    DateTime? ActualStart,
    DateTime? ActualEnd,
    double QtyGood,
    double QtyRejected,
    double? OfQuantity,
    double? QtyOrdered,
    double? Weight,
    string? StatusText,
    string? Atelier,
    string? PacketNo,
    string? CustomerCode,
    string? CustomerName,
    DateOnly? OrderDate,
    DateOnly? DeliveryDate,
    string? CustomerOrderRef,
    double? DurationSeconds,
    string DurationKind,
    string? AnomalyCode,
    string RowHash);

public static class MontepullParsers
{
    public static ParsedCommandeRow? ParseCommande(IReadOnlyList<string> row, Dictionary<string, int> map, int sourceRowNo)
    {
        var order = MontepullColumnIndex.Get(row, map, "Commande", "commande");
        var article = MontepullColumnIndex.Get(row, map, "Article", "article");
        if (string.IsNullOrWhiteSpace(order) || string.IsNullOrWhiteSpace(article))
            return null;

        MontepullColumnIndex.TryGetDouble(row, map, out var ordered, "Qte_Commandee", "qte_commandee");
        MontepullColumnIndex.TryGetDouble(row, map, out var launched, "Qte_Lancee", "qte_lancee");
        MontepullColumnIndex.TryGetDouble(row, map, out var produced, "Qte_Fabriquee_Derniere_Operation", "qte_fabriquee_derniere_operation");
        double? somme = MontepullColumnIndex.TryGetDouble(row, map, out var s, "Somme_Operations") ? s : null;
        double? s70c = MontepullColumnIndex.TryGetDouble(row, map, out var a, "Somme_Op_70_Commande") ? a : null;
        double? s70a = MontepullColumnIndex.TryGetDouble(row, map, out var b, "Somme_Op_70_Article") ? b : null;
        int? lastOp = MontepullColumnIndex.TryGetInt(row, map, out var op, "Derniere_Operation") ? op : null;
        var remLaunch = MontepullQuantityRules.RemainingToLaunch(ordered, launched);
        var remProd = MontepullQuantityRules.RemainingToProduce(ordered, produced);
        var status = MontepullQuantityRules.LaunchStatus(ordered, launched, produced);
        var hash = MontepullHash.RowHash(order, article, ordered.ToString("G17", CultureInfo.InvariantCulture), launched.ToString("G17", CultureInfo.InvariantCulture), produced.ToString("G17", CultureInfo.InvariantCulture));

        return new ParsedCommandeRow(
            sourceRowNo,
            order.Trim(),
            MontepullColumnIndex.Get(row, map, "Client")?.Trim(),
            article.Trim(),
            MontepullColumnIndex.Get(row, map, "Designation", "Designation"),
            ordered,
            launched,
            somme,
            s70c,
            s70a,
            lastOp,
            produced,
            remLaunch,
            remProd,
            status,
            hash);
    }

    public static ParsedSuiviRow? ParseSuivi(
        IReadOnlyList<string> row,
        Dictionary<string, int> map,
        int sourceRowNo,
        string sheetName,
        string sourceKind)
    {
        var ofCode = MontepullColumnIndex.Get(row, map, "N OF", "N° OF", "n_of", "No OF");
        if (string.IsNullOrWhiteSpace(ofCode))
            ofCode = MontepullColumnIndex.Get(row, map, "n of");
        // fallback: keys already normalized may be n_of
        if (string.IsNullOrWhiteSpace(ofCode) && map.TryGetValue("n_of", out var ofIdx) && ofIdx < row.Count)
            ofCode = row[ofIdx];
        if (string.IsNullOrWhiteSpace(ofCode))
            return null;

        int? op = MontepullColumnIndex.TryGetInt(row, map, out var opNo, "Operation", "Opération", "operation") ? opNo : null;
        var start = MontepullColumnIndex.GetDateTime(row, map, "Date Debut", "Date Début", "date_debut");
        var end = MontepullColumnIndex.GetDateTime(row, map, "Date Fin", "date_fin");
        MontepullColumnIndex.TryGetDouble(row, map, out var good, "Quantite Reelle", "Quantité Réelle", "quantite_reelle");
        MontepullColumnIndex.TryGetDouble(row, map, out var rejected, "Quantite Rejet", "Quantité Rejet", "quantite_rejet");
        double? ofQty = MontepullColumnIndex.TryGetDouble(row, map, out var oq, "Qte OF", "qte_of") ? oq : null;
        double? qtyOrd = MontepullColumnIndex.TryGetDouble(row, map, out var qo, "Qte commandee", "Qté commandée", "qte_commandee") ? qo : null;
        double? weight = MontepullColumnIndex.TryGetDouble(row, map, out var w, "Poids") ? w : null;
        var (seconds, kind, anomaly) = MontepullDurationAnalyzer.Analyze(start, end);
        var packet = MontepullColumnIndex.Get(row, map, "Npaquet", "npaquet");
        var hash = MontepullHash.RowHash(
            ofCode,
            op?.ToString(),
            packet,
            start?.ToString("O"),
            end?.ToString("O"),
            good.ToString("G17", CultureInfo.InvariantCulture),
            rejected.ToString("G17", CultureInfo.InvariantCulture),
            sheetName,
            sourceRowNo.ToString(CultureInfo.InvariantCulture));

        return new ParsedSuiviRow(
            sourceRowNo,
            sheetName,
            sourceKind,
            ofCode.Trim(),
            MontepullColumnIndex.Get(row, map, "N CMD", "N° CMD", "n_cmd")?.Trim(),
            MontepullColumnIndex.Get(row, map, "Article")?.Trim(),
            MontepullColumnIndex.Get(row, map, "Designation", "Désignation"),
            op,
            start,
            end,
            good,
            rejected,
            ofQty,
            qtyOrd,
            weight,
            MontepullColumnIndex.Get(row, map, "Statut"),
            MontepullColumnIndex.Get(row, map, "Atelier"),
            packet,
            MontepullColumnIndex.Get(row, map, "CMD Client", "cmd_client"),
            MontepullColumnIndex.Get(row, map, "CMD Client Name", "cmd_client_name"),
            MontepullColumnIndex.GetDate(row, map, "CMD Date", "cmd_date"),
            MontepullColumnIndex.GetDate(row, map, "CMD Date Livraison", "cmd_date_livraison"),
            MontepullColumnIndex.Get(row, map, "N CMD Client", "n_cmd_client"),
            seconds,
            kind,
            anomaly,
            hash);
    }
}

public static class MontepullRemainingLoad
{
    /// <summary>Charge restante = quantité restante × temps unitaire validé. Opération terminée → 0.</summary>
    public static double RemainingLoadHours(double remainingQty, double validatedUnitTime, string timeUnit, bool operationCompleted)
    {
        if (operationCompleted || remainingQty <= 0 || validatedUnitTime <= 0)
            return 0;

        return timeUnit.ToUpperInvariant() switch
        {
            "H" or "HOUR" or "HOURS" => remainingQty * validatedUnitTime,
            "MIN" or "MINUTE" or "MINUTES" => remainingQty * validatedUnitTime / 60.0,
            "S" or "SEC" or "SECOND" or "SECONDS" => remainingQty * validatedUnitTime / 3600.0,
            _ => remainingQty * validatedUnitTime // TO_CONFIRM: treat as hours-equivalent until validated
        };
    }
}
