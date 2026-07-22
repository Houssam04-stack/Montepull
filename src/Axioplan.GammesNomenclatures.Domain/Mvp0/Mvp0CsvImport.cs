using System.Globalization;
using System.Text;

namespace Axioplan.GammesNomenclatures.Domain.Mvp0;

public static class Mvp0DataTypes
{
    public const string Articles = "ARTICLES";
    public const string Boms = "BOMS";
    public const string Routings = "ROUTINGS";
    public const string Calendars = "CALENDARS";
    public const string WorkOrders = "WORK_ORDERS";
    public const string Centers = "CENTERS";
    public const string BomOpLinks = "BOM_OP_LINKS";
}

public sealed record Mvp0CsvPreview(
    string DataType,
    IReadOnlyList<string> Headers,
    IReadOnlyDictionary<string, string> SuggestedMapping,
    IReadOnlyList<string> RequiredMissing,
    IReadOnlyList<IReadOnlyList<string>> PreviewRows,
    int TotalDataLines);

public sealed record Mvp0CsvParseReport(
    string DataType,
    int LinesRead,
    int LinesImported,
    int LinesRejected,
    IReadOnlyList<string> Rejects,
    IReadOnlyList<string> Duplicates,
    IReadOnlyList<string> MissingFields,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Assumptions,
    IReadOnlyDictionary<string, string> MappingUsed);

/// <summary>Assistant CSV MVP-0 : détection colonnes, preview, rejette explicite (jamais silence).</summary>
public static class Mvp0CsvImportEngine
{
    private static readonly Dictionary<string, string[]> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["code"] = ["code", "article", "item", "article_code", "sku"],
        ["type"] = ["type", "article_type", "item_type"],
        ["unit"] = ["unit", "uom", "unite", "unité"],
        ["family"] = ["family", "famille", "family_code"],
        ["active"] = ["active", "actif", "is_active"],
        ["parent"] = ["parent", "parent_code", "compose", "assembled"],
        ["component"] = ["component", "component_code", "composant", "child"],
        ["qty"] = ["qty", "quantity", "qte", "quantite"],
        ["scrap"] = ["scrap", "scrap_rate", "rebut", "taux_rebut"],
        ["article"] = ["article", "article_code", "item"],
        ["op"] = ["op", "op_code", "operation", "operation_code"],
        ["seq"] = ["seq", "sequence", "seq_no"],
        ["center"] = ["center", "centre", "work_center", "cc"],
        ["cycle"] = ["cycle", "cycle_time", "temps_cycle"],
        ["setup"] = ["setup", "setup_time", "temps_setup"],
        ["cal"] = ["calendar", "calendrier", "cal_code", "code"],
        ["start"] = ["start", "debut", "start_time", "from"],
        ["end"] = ["end", "fin", "end_time", "to"],
        ["team"] = ["team", "equipe", "shift"],
        ["trs"] = ["trs", "oee", "efficiency"],
        ["wo"] = ["wo", "of", "wo_code", "order"],
        ["planned_end"] = ["planned_end", "date_prevue", "due_plan"],
        ["actual_end"] = ["actual_end", "date_reelle", "due_actual"],
        ["planned_qty"] = ["planned_qty", "qty_plan", "qte_prevue"],
        ["good_qty"] = ["good_qty", "qty_good", "qte_bonne"],
        ["scrap_qty"] = ["scrap_qty", "qty_scrap", "qte_rebut"],
        ["planned_hours"] = ["planned_hours", "duree_prevue", "std_hours"],
        ["actual_hours"] = ["actual_hours", "duree_reelle", "act_hours"]
    };

    public static IReadOnlyList<string> RequiredFields(string dataType) => dataType.ToUpperInvariant() switch
    {
        Mvp0DataTypes.Articles => ["code", "type", "unit", "family"],
        Mvp0DataTypes.Boms => ["parent", "component", "qty"],
        Mvp0DataTypes.Routings => ["article", "op", "seq", "center", "cycle"],
        Mvp0DataTypes.Calendars => ["cal", "start", "end", "trs"],
        Mvp0DataTypes.WorkOrders => ["wo", "article", "planned_end", "actual_end", "planned_qty", "good_qty", "scrap_qty", "planned_hours", "actual_hours"],
        Mvp0DataTypes.Centers => ["center"],
        Mvp0DataTypes.BomOpLinks => ["parent", "component"],
        _ => []
    };

    public static Mvp0CsvPreview Preview(string dataType, string csvContent, int previewLimit = 8)
    {
        var (headers, rows) = ReadTable(csvContent);
        var mapping = AutoMap(headers);
        var required = RequiredFields(dataType);
        var missing = required.Where(r => !mapping.ContainsKey(r)).ToList();
        var preview = rows.Take(previewLimit).ToList();
        return new Mvp0CsvPreview(dataType, headers, mapping, missing, preview, rows.Count);
    }

    public static (Mvp0CsvParseReport Report, object Payload) Parse(
        string dataType,
        string csvContent,
        IReadOnlyDictionary<string, string>? mappingOverride,
        string provenance)
    {
        var (headers, rows) = ReadTable(csvContent);
        var mapping = mappingOverride is null || mappingOverride.Count == 0
            ? AutoMap(headers)
            : new Dictionary<string, string>(mappingOverride, StringComparer.OrdinalIgnoreCase);
        var required = RequiredFields(dataType);
        var missingRequired = required.Where(r => !mapping.ContainsKey(r)).ToList();
        var rejects = new List<string>();
        var dups = new List<string>();
        var missingFields = new List<string>();
        var warnings = new List<string>();
        var assumptions = new List<string> { "Séparateur auto ; ou ,", "TRS convention [0;1] si ≤1 sinon /100 TO_CONFIRM" };

        if (missingRequired.Count > 0)
        {
            rejects.Add("Colonnes obligatoires manquantes: " + string.Join(", ", missingRequired));
            return (new Mvp0CsvParseReport(dataType, rows.Count, 0, rows.Count + 1, rejects, dups, missingRequired, warnings, assumptions, mapping),
                Array.Empty<object>());
        }

        object payload = dataType.ToUpperInvariant() switch
        {
            Mvp0DataTypes.Articles => ParseArticles(headers, rows, mapping, provenance, rejects, dups, missingFields),
            Mvp0DataTypes.Boms => ParseBoms(headers, rows, mapping, rejects, dups, missingFields),
            Mvp0DataTypes.Routings => ParseRoutings(headers, rows, mapping, rejects, dups),
            Mvp0DataTypes.Calendars => ParseCalendars(headers, rows, mapping, rejects, warnings),
            Mvp0DataTypes.WorkOrders => ParseWos(headers, rows, mapping, provenance, rejects),
            Mvp0DataTypes.Centers => ParseCenters(headers, rows, mapping, rejects),
            Mvp0DataTypes.BomOpLinks => ParseLinks(headers, rows, mapping, rejects),
            _ => RejectUnknown(dataType, rows.Count, rejects)
        };

        var imported = CountImported(payload);
        // every blank/unknown line already in rejects
        var report = new Mvp0CsvParseReport(
            dataType, rows.Count, imported, rejects.Count, rejects, dups, missingFields, warnings, assumptions, mapping);
        return (report, payload);
    }

    private static object RejectUnknown(string dataType, int lines, List<string> rejects)
    {
        rejects.Add($"Type de données inconnu: {dataType} — aucune ligne importée");
        return Array.Empty<object>();
    }

    private static int CountImported(object payload) => payload switch
    {
        ICollection<Mvp0ArticleRow> a => a.Count,
        ICollection<Mvp0BomRow> b => b.Count,
        ICollection<Mvp0RoutingOpRow> r => r.Count,
        ICollection<Mvp0CalendarRow> c => c.Count,
        ICollection<Mvp0WorkOrderActual> w => w.Count,
        ICollection<string> s => s.Count,
        _ => 0
    };

    private static List<Mvp0ArticleRow> ParseArticles(
        IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<string>> rows,
        IReadOnlyDictionary<string, string> map, string prov,
        List<string> rejects, List<string> dups, List<string> missingFields)
    {
        var list = new List<Mvp0ArticleRow>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < rows.Count; i++)
        {
            var line = i + 2;
            var row = rows[i];
            if (IsBlank(row))
            {
                rejects.Add($"Ligne {line}: ligne vide / inconnue rejetée");
                continue;
            }

            var code = Cell(headers, row, map, "code");
            if (string.IsNullOrWhiteSpace(code))
            {
                rejects.Add($"Ligne {line}: code article manquant — rejetée");
                continue;
            }

            if (!seen.Add(code)) dups.Add(code);
            var unit = Cell(headers, row, map, "unit");
            if (string.IsNullOrWhiteSpace(unit)) missingFields.Add($"Ligne {line}: unit");
            list.Add(new Mvp0ArticleRow(
                code,
                Cell(headers, row, map, "type"),
                unit,
                Cell(headers, row, map, "family"),
                ParseBool(Cell(headers, row, map, "active"), true),
                prov,
                line));
        }

        return list;
    }

    private static List<Mvp0BomRow> ParseBoms(
        IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<string>> rows,
        IReadOnlyDictionary<string, string> map, List<string> rejects, List<string> dups, List<string> missingFields)
    {
        var list = new List<Mvp0BomRow>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < rows.Count; i++)
        {
            var line = i + 2;
            var row = rows[i];
            if (IsBlank(row)) { rejects.Add($"Ligne {line}: ligne inconnue rejetée"); continue; }
            var parent = Cell(headers, row, map, "parent");
            var comp = Cell(headers, row, map, "component");
            if (string.IsNullOrWhiteSpace(parent) || string.IsNullOrWhiteSpace(comp))
            {
                rejects.Add($"Ligne {line}: parent/composant manquant — rejetée");
                continue;
            }

            var key = parent + "|" + comp;
            if (!seen.Add(key)) dups.Add(key);
            if (!TryParseDouble(Cell(headers, row, map, "qty"), out var qty))
            {
                rejects.Add($"Ligne {line}: quantité ambiguë — rejetée");
                continue;
            }

            var unit = Cell(headers, row, map, "unit");
            if (string.IsNullOrWhiteSpace(unit)) missingFields.Add($"Ligne {line}: unit");
            TryParseDouble(Cell(headers, row, map, "scrap"), out var scrap);
            list.Add(new Mvp0BomRow(parent, comp, qty, unit, scrap, line));
        }

        return list;
    }

    private static List<Mvp0RoutingOpRow> ParseRoutings(
        IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<string>> rows,
        IReadOnlyDictionary<string, string> map, List<string> rejects, List<string> dups)
    {
        var list = new List<Mvp0RoutingOpRow>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < rows.Count; i++)
        {
            var line = i + 2;
            var row = rows[i];
            if (IsBlank(row)) { rejects.Add($"Ligne {line}: ligne inconnue rejetée"); continue; }
            var article = Cell(headers, row, map, "article");
            var op = Cell(headers, row, map, "op");
            if (string.IsNullOrWhiteSpace(article) || string.IsNullOrWhiteSpace(op))
            {
                rejects.Add($"Ligne {line}: article/op manquant — rejetée");
                continue;
            }

            var key = article + "|" + op;
            if (!seen.Add(key)) dups.Add(key);
            if (!int.TryParse(Cell(headers, row, map, "seq"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var seq))
            {
                rejects.Add($"Ligne {line}: séquence ambiguë — rejetée");
                continue;
            }

            TryParseDouble(Cell(headers, row, map, "cycle"), out var cycle);
            TryParseDouble(Cell(headers, row, map, "setup"), out var setup);
            list.Add(new Mvp0RoutingOpRow(article, op, seq, Cell(headers, row, map, "center"), cycle, setup, line));
        }

        return list;
    }

    private static List<Mvp0CalendarRow> ParseCalendars(
        IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<string>> rows,
        IReadOnlyDictionary<string, string> map, List<string> rejects, List<string> warnings)
    {
        var list = new List<Mvp0CalendarRow>();
        for (var i = 0; i < rows.Count; i++)
        {
            var line = i + 2;
            var row = rows[i];
            if (IsBlank(row)) { rejects.Add($"Ligne {line}: ligne inconnue rejetée"); continue; }
            var code = Cell(headers, row, map, "cal");
            if (string.IsNullOrWhiteSpace(code)) { rejects.Add($"Ligne {line}: calendrier manquant — rejetée"); continue; }
            if (!TryParseTime(Cell(headers, row, map, "start"), out var start) || !TryParseTime(Cell(headers, row, map, "end"), out var end))
            {
                rejects.Add($"Ligne {line}: horaires ambigus — rejetée");
                continue;
            }

            TryParseDouble(Cell(headers, row, map, "trs"), out var trs);
            if (trs > 1) { warnings.Add($"Ligne {line}: TRS>{trs} interprété /100"); trs /= 100; }
            list.Add(new Mvp0CalendarRow(code, start, end, Cell(headers, row, map, "team"), trs, line));
        }

        return list;
    }

    private static List<Mvp0WorkOrderActual> ParseWos(
        IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<string>> rows,
        IReadOnlyDictionary<string, string> map, string prov, List<string> rejects)
    {
        var list = new List<Mvp0WorkOrderActual>();
        for (var i = 0; i < rows.Count; i++)
        {
            var line = i + 2;
            var row = rows[i];
            if (IsBlank(row)) { rejects.Add($"Ligne {line}: ligne inconnue rejetée"); continue; }
            var wo = Cell(headers, row, map, "wo");
            var article = Cell(headers, row, map, "article");
            if (string.IsNullOrWhiteSpace(wo) || string.IsNullOrWhiteSpace(article))
            {
                rejects.Add($"Ligne {line}: OF/article manquant — rejetée");
                continue;
            }

            if (!TryParseDate(Cell(headers, row, map, "planned_end"), out var pe)
                || !TryParseDate(Cell(headers, row, map, "actual_end"), out var ae))
            {
                rejects.Add($"Ligne {line}: dates ambiguës — rejetée");
                continue;
            }

            TryParseDouble(Cell(headers, row, map, "planned_qty"), out var pq);
            TryParseDouble(Cell(headers, row, map, "good_qty"), out var gq);
            TryParseDouble(Cell(headers, row, map, "scrap_qty"), out var sq);
            TryParseDouble(Cell(headers, row, map, "planned_hours"), out var ph);
            TryParseDouble(Cell(headers, row, map, "actual_hours"), out var ah);
            list.Add(new Mvp0WorkOrderActual(wo, article, pe, ae, pq, gq, sq, ph, ah, prov));
        }

        return list;
    }

    private static List<string> ParseCenters(
        IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<string>> rows,
        IReadOnlyDictionary<string, string> map, List<string> rejects)
    {
        var list = new List<string>();
        for (var i = 0; i < rows.Count; i++)
        {
            var line = i + 2;
            var row = rows[i];
            if (IsBlank(row)) { rejects.Add($"Ligne {line}: ligne inconnue rejetée"); continue; }
            var c = Cell(headers, row, map, "center");
            if (string.IsNullOrWhiteSpace(c)) { rejects.Add($"Ligne {line}: centre manquant — rejetée"); continue; }
            list.Add(c);
        }

        return list;
    }

    private static List<string> ParseLinks(
        IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<string>> rows,
        IReadOnlyDictionary<string, string> map, List<string> rejects)
    {
        var list = new List<string>();
        for (var i = 0; i < rows.Count; i++)
        {
            var line = i + 2;
            var row = rows[i];
            if (IsBlank(row)) { rejects.Add($"Ligne {line}: ligne inconnue rejetée"); continue; }
            var p = Cell(headers, row, map, "parent");
            var c = Cell(headers, row, map, "component");
            if (string.IsNullOrWhiteSpace(p) || string.IsNullOrWhiteSpace(c))
            {
                rejects.Add($"Ligne {line}: liaison incomplète — rejetée");
                continue;
            }

            list.Add(p + "|" + c);
        }

        return list;
    }

    public static Dictionary<string, string> AutoMap(IReadOnlyList<string> headers)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (field, aliases) in Aliases)
        {
            foreach (var h in headers)
            {
                var norm = Normalize(h);
                if (aliases.Any(a => Normalize(a) == norm))
                {
                    map[field] = h;
                    break;
                }
            }
        }

        return map;
    }

    private static (IReadOnlyList<string> Headers, IReadOnlyList<IReadOnlyList<string>> Rows) ReadTable(string csv)
    {
        var lines = csv.Replace("\r\n", "\n").Replace('\r', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length == 0) return ([], []);
        var sep = lines[0].Count(c => c == ';') >= lines[0].Count(c => c == ',') ? ';' : ',';
        var headers = Split(lines[0], sep);
        var rows = new List<IReadOnlyList<string>>();
        for (var i = 1; i < lines.Length; i++)
            rows.Add(Split(lines[i], sep));
        return (headers, rows);
    }

    private static List<string> Split(string line, char sep)
    {
        var parts = new List<string>();
        var sb = new StringBuilder();
        var inQ = false;
        foreach (var ch in line)
        {
            if (ch == '"') { inQ = !inQ; continue; }
            if (ch == sep && !inQ) { parts.Add(sb.ToString().Trim()); sb.Clear(); continue; }
            sb.Append(ch);
        }

        parts.Add(sb.ToString().Trim());
        return parts;
    }

    private static string Normalize(string s)
        => new string(s.Trim().ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());

    private static string? Cell(IReadOnlyList<string> headers, IReadOnlyList<string> row, IReadOnlyDictionary<string, string> map, string field)
    {
        if (!map.TryGetValue(field, out var header)) return null;
        var idx = -1;
        for (var i = 0; i < headers.Count; i++)
            if (headers[i].Equals(header, StringComparison.OrdinalIgnoreCase)) { idx = i; break; }
        if (idx < 0 || idx >= row.Count) return null;
        return string.IsNullOrWhiteSpace(row[idx]) ? null : row[idx].Trim();
    }

    private static bool IsBlank(IReadOnlyList<string> row) => row.All(string.IsNullOrWhiteSpace);

    private static bool TryParseDouble(string? s, out double v)
        => double.TryParse(s?.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out v);

    private static bool TryParseDate(string? s, out DateOnly d)
    {
        if (DateOnly.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out d)) return true;
        if (DateOnly.TryParse(s, new CultureInfo("fr-FR"), DateTimeStyles.None, out d)) return true;
        return false;
    }

    private static bool TryParseTime(string? s, out TimeOnly t)
    {
        if (TimeOnly.TryParse(s, CultureInfo.InvariantCulture, out t)) return true;
        if (TimeOnly.TryParse(s, new CultureInfo("fr-FR"), out t)) return true;
        return false;
    }

    private static bool ParseBool(string? s, bool defaultValue)
    {
        if (string.IsNullOrWhiteSpace(s)) return defaultValue;
        return s is "1" or "true" or "True" or "oui" or "OUI" or "yes";
    }
}
