using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace Axioplan.GammesNomenclatures.Infrastructure.Imports;

/// <summary>
/// Parse les bons de commande PDF type PROMOD (ex. YBONSO.pdf) :
/// Commande N°, client, date de mise à disposition, modèle, couleur, tailles/quantités.
/// </summary>
public static class PromodPurchaseOrderPdfParser
{
    private static readonly string[] KnownSizes =
    [
        "XXXS", "XXS", "XS", "S", "M", "L", "XL", "XXL", "XXXL",
        "34", "36", "38", "40", "42", "44", "46", "48", "50"
    ];

    public sealed record ParsedLine(
        string? LineReference,
        string Model,
        string Size,
        string Color,
        int Quantity,
        double? UnitPrice,
        double? LineAmount,
        string? SupplierRef);

    public sealed record ParsedOrder(
        string OrderNumber,
        string Customer,
        string? Supplier,
        DateTime? CreatedOn,
        DateTime? DueDate,
        string Model,
        string Color,
        string? SupplierRef,
        IReadOnlyList<ParsedLine> Lines);

    public static bool TryParse(byte[] content, out ParsedOrder? order, out string? error)
    {
        order = null;
        error = null;
        try
        {
            using var stream = new MemoryStream(content);
            using var document = PdfDocument.Open(stream);
            var allWords = new List<Word>();
            var lines = new List<ParsedLine>();
            foreach (var page in document.GetPages())
            {
                var pageWords = page.GetWords().ToList();
                allWords.AddRange(pageWords);
                lines.AddRange(ExtractLinesFromPage(pageWords));
            }

            if (allWords.Count == 0)
            {
                error = "PDF sans texte extractible.";
                return false;
            }

            var text = string.Join(' ', allWords.Select(w => w.Text));
            if (!LooksLikePromodOrder(text))
            {
                return false;
            }

            var orderNumber = MatchGroup(text, @"Commande\s*N[°ºo]\s*([A-Z0-9\-]+)", 1)
                ?? MatchGroup(text, @"\b(SO\d{6,})\b", 1);
            if (string.IsNullOrWhiteSpace(orderNumber))
            {
                error = "Numéro de commande introuvable (attendu : Commande N° SOxxxxxx).";
                return false;
            }

            var customer = DetectCustomer(allWords, text);
            var supplier = DetectSupplier(text);
            var dueDate = ParseFrenchDate(MatchGroup(text, @"Date\s+de\s+mise\s+[àa]\s+disposition\s*:\s*(\d{2}/\d{2}/\d{4})", 1));
            var createdOn = ParseFrenchDate(MatchGroup(text, @"Cr[ée]{1,2}e\s+le\s+(\d{2}/\d{2}/\d{4})", 1));

            if (lines.Count == 0)
            {
                lines = ExtractLinesFallback(RebuildReadingOrder(allWords));
            }

            lines = DeduplicateBySize(lines);
            if (lines.Count == 0)
            {
                error = "Aucune ligne taille/quantité détectée dans le bon de commande.";
                return false;
            }

            var model = lines.Select(l => l.Model).FirstOrDefault(m => !string.IsNullOrWhiteSpace(m) && m != "ARTICLE") ?? "ARTICLE";
            var color = lines.Select(l => l.Color).FirstOrDefault(c => !string.IsNullOrWhiteSpace(c)) ?? "";
            var supplierRef = lines.Select(l => l.SupplierRef).FirstOrDefault(r => !string.IsNullOrWhiteSpace(r));

            order = new ParsedOrder(
                orderNumber.Trim().ToUpperInvariant(),
                customer,
                supplier,
                createdOn,
                dueDate,
                model.Trim().ToUpperInvariant(),
                color.Trim().ToUpperInvariant(),
                supplierRef,
                lines);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// Feuille canonique pour l'import commande existant (métadonnées + tailles + quantités).
    /// </summary>
    public static UniversalWorkbookLoader.Sheet ToCanonicalSheet(ParsedOrder order)
    {
        var sizes = order.Lines.Select(l => l.Size).ToList();
        var qtys = order.Lines.Select(l => l.Quantity.ToString(CultureInfo.InvariantCulture)).ToList();

        var labelRow = new List<string> { "Client", "Ref commande", "Article", "Delai", "Couleur", "Fournisseur", "Ref fournisseur" };
        var valueRow = new List<string>
        {
            order.Customer,
            order.OrderNumber,
            order.Model,
            order.DueDate?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "",
            order.Color,
            order.Supplier ?? "",
            order.SupplierRef ?? ""
        };

        var sizeRow = new List<string> { "Tailles" };
        sizeRow.AddRange(sizes);
        var qtyRow = new List<string> { "Total commande" };
        qtyRow.AddRange(qtys);

        var detailHeader = new List<string> { "Ref ligne", "Modele", "Taille", "Couleur", "Quantite", "PU", "Montant", "Ref fournisseur" };
        var rows = new List<IReadOnlyList<string>>
        {
            labelRow,
            valueRow,
            sizeRow,
            qtyRow,
            new List<string>(),
            detailHeader
        };

        foreach (var line in order.Lines)
        {
            rows.Add([
                line.LineReference ?? "",
                line.Model,
                line.Size,
                line.Color,
                line.Quantity.ToString(CultureInfo.InvariantCulture),
                line.UnitPrice?.ToString("0.##", CultureInfo.InvariantCulture) ?? "",
                line.LineAmount?.ToString("0.##", CultureInfo.InvariantCulture) ?? "",
                line.SupplierRef ?? ""
            ]);
        }

        var colCount = rows.Max(r => r.Count);
        for (var i = 0; i < rows.Count; i++)
        {
            if (rows[i].Count >= colCount)
            {
                continue;
            }

            var padded = rows[i].ToList();
            while (padded.Count < colCount)
            {
                padded.Add("");
            }

            rows[i] = padded;
        }

        return new UniversalWorkbookLoader.Sheet("BonDeCommande", rows, colCount);
    }

    private static bool LooksLikePromodOrder(string text)
    {
        var t = text.ToUpperInvariant();
        return t.Contains("BON DE COMMANDE", StringComparison.Ordinal)
               || (t.Contains("COMMANDE N", StringComparison.Ordinal) && t.Contains("MISE À DISPOSITION", StringComparison.Ordinal))
               || (t.Contains("COMMANDE N", StringComparison.Ordinal) && t.Contains("MISE A DISPOSITION", StringComparison.Ordinal))
               || (Regex.IsMatch(t, @"\bSO\d{6,}\b") && t.Contains("TAILLE", StringComparison.Ordinal));
    }

    private static readonly HashSet<string> CustomerBlocklist = new(StringComparer.OrdinalIgnoreCase)
    {
        "PLEASE", "THIS", "ACCEPTANCE", "RECEIPT", "AFTER", "MASTER", "AGREEMENT", "SUPPLIER",
        "PURCHASER", "CONDITIONS", "INCOTERM", "ORGANISE", "INSPECTION", "TRANSFER", "PROPERTY",
        "MONTE", "PULL", "MOROCCO", "MAROC", "FRANCE", "LILLE", "CASABLANCA", "LISSASFA",
        "CHEMIN", "ROUTE", "ADRESSE", "LIVRAISON", "FACTURATION", "MODELE", "DESCRIPTION",
        "TAILLES", "COULEUR", "QUANTITE", "TOTAL", "IMPRIMÉ", "IMPRIME", "SAISIE", "MODIFIÉE",
        "MODIFIEE", "CRÉÉE", "CREEE", "COMMANDE"
    };

    private static string DetectCustomer(IReadOnlyList<Word> words, string text)
    {
        // Ligne d'en-tête : "PROMOD Adresse de livraison" / "BONOBO Adresse de livraison"
        var beforeAddress = MatchGroup(text, @"\b([A-Z][A-Z0-9\-']{2,30})\s+Adresse\s+de\s+livraison", 1);
        if (!string.IsNullOrWhiteSpace(beforeAddress) && IsPlausibleCustomer(beforeAddress))
        {
            return beforeAddress.ToUpperInvariant();
        }

        // "Fax PROMOD :" / "Fax BONOBO :"
        var faxCustomer = MatchGroup(text, @"Fax\s+([A-Z][A-Z0-9\-']{2,30})\s*:", 1);
        if (!string.IsNullOrWhiteSpace(faxCustomer) && IsPlausibleCustomer(faxCustomer))
        {
            return faxCustomer.ToUpperInvariant();
        }

        // Clients connus fréquents (Montepull).
        foreach (var known in new[] { "PROMOD", "BONOBO" })
        {
            if (Regex.IsMatch(text, $@"\b{known}\b", RegexOptions.IgnoreCase))
            {
                return known;
            }
        }

        // Colonne gauche de la 1re page uniquement (éviter le texte juridique anglais en page 2).
        var page1Top = words
            .Where(w => w.BoundingBox.Bottom > 280 && w.BoundingBox.Left < 200)
            .OrderByDescending(w => w.BoundingBox.Bottom)
            .Select(w => w.Text.Trim())
            .Where(t => t.Length >= 3)
            .ToList();

        foreach (var candidate in page1Top)
        {
            if (!IsPlausibleCustomer(candidate))
            {
                continue;
            }

            if (candidate.StartsWith("Commande", StringComparison.OrdinalIgnoreCase)
                || candidate.StartsWith("Modifi", StringComparison.OrdinalIgnoreCase)
                || candidate.Equals("BON", StringComparison.OrdinalIgnoreCase)
                || candidate.StartsWith("BON DE", StringComparison.OrdinalIgnoreCase)
                || candidate.StartsWith("Mode", StringComparison.OrdinalIgnoreCase)
                || candidate.StartsWith("Date", StringComparison.OrdinalIgnoreCase)
                || candidate.StartsWith("Chemin", StringComparison.OrdinalIgnoreCase)
                || candidate.StartsWith("Adresse", StringComparison.OrdinalIgnoreCase)
                || candidate.StartsWith("Fax", StringComparison.OrdinalIgnoreCase)
                || candidate.StartsWith("Route", StringComparison.OrdinalIgnoreCase)
                || IsSizeToken(candidate)
                || Regex.IsMatch(candidate, @"^PE\d+")
                || Regex.IsMatch(candidate, @"^\d"))
            {
                continue;
            }

            if (candidate.All(c => char.IsLetter(c) || c is '-' or '\'' ) && candidate.Length <= 40)
            {
                return candidate.ToUpperInvariant();
            }
        }

        return "CLIENT_IMPORT";
    }

    private static bool IsPlausibleCustomer(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var token = value.Trim();
        if (CustomerBlocklist.Contains(token))
        {
            return false;
        }

        // Ne pas confondre avec "BON DE COMMANDE".
        if (token.StartsWith("BON DE", StringComparison.OrdinalIgnoreCase)
            || token.Equals("BON", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return token.Length is >= 3 and <= 40;
    }

    private static string? DetectSupplier(string text)
    {
        if (Regex.IsMatch(text, @"MONTE\s*PULL", RegexOptions.IgnoreCase))
        {
            return "MONTE PULL";
        }

        return null;
    }

    private static List<ParsedLine> ExtractLinesFromPage(IReadOnlyList<Word> pageWords)
    {
        var lineGroups = GroupWordsByLine(pageWords);
        var lines = new List<ParsedLine>();

        for (var i = 0; i < lineGroups.Count; i++)
        {
            var lineText = string.Join(' ', lineGroups[i].Select(w => w.Text)).Trim();
            if (!Regex.IsMatch(lineText, @"^PE\d{5,}$", RegexOptions.IgnoreCase))
            {
                continue;
            }

            var lineRef = lineText.ToUpperInvariant();
            var next = i + 1 < lineGroups.Count
                ? string.Join(' ', lineGroups[i + 1].Select(w => w.Text)).Trim()
                : "";
            var next2 = i + 2 < lineGroups.Count
                ? string.Join(' ', lineGroups[i + 2].Select(w => w.Text)).Trim()
                : "";
            var next3 = i + 3 < lineGroups.Count
                ? string.Join(' ', lineGroups[i + 3].Select(w => w.Text)).Trim()
                : "";

            // Ex: "MBERTILLE XS 238 10,70 2 546,60" ou "MBERTILLE S MAUVE 782 10,70 8 367,40"
            var detail = Regex.Match(
                next,
                @"^(?<model>.+?)\s+(?<size>XXXS|XXS|XS|S|M|L|XL|XXL|XXXL|\d{2})(?:\s+(?<color>[A-ZÉÈÊËÀÂÄÙÛÜÔÖÇ\-]+))?\s+(?<qty>\d{1,5})\s+(?<pu>\d{1,3}[.,]\d{2})\s+(?<amt>[\d\s]+[.,]\d{2})\s*$",
                RegexOptions.IgnoreCase);
            if (!detail.Success)
            {
                continue;
            }

            var size = detail.Groups["size"].Value.ToUpperInvariant();
            var model = NormalizeModel(detail.Groups["model"].Value, size, detail.Groups["color"].Value);
            var color = detail.Groups["color"].Success && !string.IsNullOrWhiteSpace(detail.Groups["color"].Value)
                ? NormalizeColor(detail.Groups["color"].Value)
                : "";

            // Couleur souvent sur la ligne suivante : "MAUVE XS"
            if (string.IsNullOrWhiteSpace(color))
            {
                var colorLine = Regex.Match(
                    next2,
                    @"^(?<color>[A-ZÉÈÊËÀÂÄÙÛÜÔÖÇ\-]+)\s+(?<size>XXXS|XXS|XS|S|M|L|XL|XXL|XXXL|\d{2})\s*$",
                    RegexOptions.IgnoreCase);
                if (colorLine.Success)
                {
                    color = NormalizeColor(colorLine.Groups["color"].Value);
                }
            }

            if (string.IsNullOrWhiteSpace(color))
            {
                var colorOnly = Regex.Match(next2, @"^(?<color>[A-ZÉÈÊËÀÂÄÙÛÜÔÖÇ\-]+)\s*$", RegexOptions.IgnoreCase);
                if (colorOnly.Success && !Regex.IsMatch(colorOnly.Groups["color"].Value, @"^CL\d+", RegexOptions.IgnoreCase))
                {
                    color = NormalizeColor(colorOnly.Groups["color"].Value);
                }
            }

            string? supplierRef = null;
            foreach (var candidate in new[] { next2, next3 })
            {
                var sref = Regex.Match(candidate, @"^(?<sref>CL\d+)\s*$", RegexOptions.IgnoreCase);
                if (sref.Success)
                {
                    supplierRef = sref.Groups["sref"].Value.ToUpperInvariant();
                    break;
                }
            }

            var qty = int.Parse(detail.Groups["qty"].Value, CultureInfo.InvariantCulture);
            var pu = ParseFrNumber(detail.Groups["pu"].Value);
            var amt = ParseFrNumber(detail.Groups["amt"].Value);

            lines.Add(new ParsedLine(lineRef, model, size, color, qty, pu, amt, supplierRef));
        }

        return lines;
    }

    private static List<List<Word>> GroupWordsByLine(IReadOnlyList<Word> words)
    {
        const double yTolerance = 2.5;
        var ordered = words
            .OrderByDescending(w => w.BoundingBox.Bottom)
            .ThenBy(w => w.BoundingBox.Left)
            .ToList();

        var lines = new List<List<Word>>();
        foreach (var word in ordered)
        {
            var y = word.BoundingBox.Bottom;
            var line = lines.LastOrDefault();
            if (line is null || Math.Abs(line[0].BoundingBox.Bottom - y) > yTolerance)
            {
                lines.Add([word]);
            }
            else
            {
                line.Add(word);
            }
        }

        foreach (var line in lines)
        {
            line.Sort((a, b) => a.BoundingBox.Left.CompareTo(b.BoundingBox.Left));
        }

        return lines;
    }

    private static List<ParsedLine> ExtractLinesFallback(string text)
    {
        var lines = new List<ParsedLine>();
        var qtyBeforePe = Regex.Matches(
            text,
            @"(?<size>XXXS|XXS|XS|S|M|L|XL|XXL|XXXL|\d{2})\s+(?<color>[A-ZÉÈÊËÀÂÄÙÛÜÔÖÇ\-]+)\s+(?<qty>\d{1,5})\s*(?<ref>PE\d{5,})",
            RegexOptions.IgnoreCase);

        foreach (Match match in qtyBeforePe)
        {
            lines.Add(new ParsedLine(
                match.Groups["ref"].Value.ToUpperInvariant(),
                "ARTICLE",
                match.Groups["size"].Value.ToUpperInvariant(),
                NormalizeColor(match.Groups["color"].Value),
                int.Parse(match.Groups["qty"].Value, CultureInfo.InvariantCulture),
                null,
                null,
                null));
        }

        // Compléter modèle depuis "PE... MBERTILLE SIZE"
        var models = Regex.Matches(
            text,
            @"(?<ref>PE\d{5,})\s+(?<model>[A-Z][A-Z0-9\-]+)\s+(?<size>XXXS|XXS|XS|S|M|L|XL|XXL|XXXL|\d{2})",
            RegexOptions.IgnoreCase);
        var modelByRef = models.Cast<Match>()
            .GroupBy(m => m.Groups["ref"].Value.ToUpperInvariant())
            .ToDictionary(g => g.Key, g => g.First().Groups["model"].Value.ToUpperInvariant());

        return DeduplicateBySize(lines.Select(l =>
            l with { Model = l.LineReference is not null && modelByRef.TryGetValue(l.LineReference, out var m) ? m : l.Model }));
    }

    private static List<ParsedLine> DeduplicateBySize(IEnumerable<ParsedLine> lines)
        => lines
            .GroupBy(l => l.Size, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(x => x.Quantity).First())
            .OrderBy(l => Array.IndexOf(KnownSizes, l.Size.ToUpperInvariant()) is int i and >= 0 ? i : 99)
            .ThenBy(l => l.Size)
            .ToList();

    private static string RebuildReadingOrder(IReadOnlyList<Word> words)
    {
        const double yTol = 3.0;
        var ordered = words
            .OrderByDescending(w => w.BoundingBox.Bottom)
            .ThenBy(w => w.BoundingBox.Left)
            .ToList();

        var sb = new StringBuilder();
        double? lastY = null;
        foreach (var word in ordered)
        {
            var y = word.BoundingBox.Bottom;
            if (lastY is double ly && Math.Abs(ly - y) > yTol)
            {
                sb.Append(' ');
            }
            else if (sb.Length > 0)
            {
                sb.Append(' ');
            }

            sb.Append(word.Text);
            lastY = y;
        }

        return sb.ToString();
    }

    private static string NormalizeModel(string raw, string size, string color)
    {
        var tokens = raw.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(t => !t.Equals(size, StringComparison.OrdinalIgnoreCase))
            .Where(t => !t.Equals(color, StringComparison.OrdinalIgnoreCase))
            .Where(t => !Regex.IsMatch(t, @"^CL\d+", RegexOptions.IgnoreCase))
            .Where(t => !Regex.IsMatch(t, @"^PE\d+", RegexOptions.IgnoreCase))
            .Where(t => !Regex.IsMatch(t, @"^\d+[.,]\d{2}$"))
            .ToList();

        var model = string.Join(' ', tokens).Trim();
        return string.IsNullOrWhiteSpace(model) ? "ARTICLE" : model.ToUpperInvariant();
    }

    private static string NormalizeColor(string raw)
    {
        var cleaned = Regex.Replace(raw.Trim(), @"\s+", " ");
        var token = cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? cleaned;
        token = token.ToUpperInvariant();

        // PDF colle souvent "ROUGECLAIR", "BLEUMARINE", "VERTSAUGE"...
        token = Regex.Replace(
            token,
            @"(ROUGE|BLEU|VERT|GRIS|BEIGE|ROSE|JAUNE|ORANGE|MARRON|NOIR|BLANC)(CLAIR|FONCE|FONCÉ|MARINE|SAUGE|NUIT|PASTEL)$",
            "$1 $2",
            RegexOptions.IgnoreCase);

        return token.Replace("FONCÉ", "FONCE", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSizeToken(string value)
        => KnownSizes.Any(s => s.Equals(value.Trim(), StringComparison.OrdinalIgnoreCase));

    private static string? MatchGroup(string text, string pattern, int group)
    {
        var m = Regex.Match(text, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return m.Success ? m.Groups[group].Value.Trim() : null;
    }

    private static DateTime? ParseFrenchDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (DateTime.TryParseExact(value.Trim(), ["dd/MM/yyyy", "d/M/yyyy"], CultureInfo.GetCultureInfo("fr-FR"), DateTimeStyles.None, out var dt))
        {
            return dt.Date;
        }

        return null;
    }

    private static double? ParseFrNumber(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var cleaned = value.Replace(" ", "", StringComparison.Ordinal).Replace(",", ".", StringComparison.Ordinal);
        return double.TryParse(cleaned, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n : null;
    }
}
