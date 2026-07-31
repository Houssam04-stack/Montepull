using System.Data;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using ExcelDataReader;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace Axioplan.GammesNomenclatures.Infrastructure.Imports;

/// <summary>
/// Charge un fichier tabulaire depuis Excel, CSV, Word (.docx), PDF, ou export Google Docs/Sheets.
/// Produit des feuilles (lignes × colonnes texte) consommables par les pipelines d'import existants.
/// </summary>
public static class UniversalWorkbookLoader
{
    public const string AcceptedExtensionsHint =
        ".xlsx, .xls, .csv, .tsv, .txt, .docx, .pdf (exports Google Docs/Sheets inclus)";

    public const string HtmlAcceptAttribute =
        ".xlsx,.xls,.csv,.tsv,.txt,.docx,.pdf," +
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet," +
        "application/vnd.ms-excel," +
        "text/csv,text/plain," +
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document," +
        "application/pdf";

    public sealed record Sheet(string Name, List<IReadOnlyList<string>> Rows, int ColumnCount);

    public static IReadOnlyList<Sheet> Load(byte[] fileContent, string? fileName = null)
    {
        if (fileContent is null || fileContent.Length == 0)
        {
            throw new InvalidOperationException(
                $"Fichier vide ou illisible. Formats supportés : {AcceptedExtensionsHint}.");
        }

        var ext = NormalizeExtension(fileName);
        RejectGoogleShortcut(ext, fileName);

        try
        {
            var sheets = DetectAndLoad(fileContent, ext, fileName);
            if (sheets.Count == 0 || sheets.All(s => s.Rows.Count == 0))
            {
                throw new InvalidOperationException("Aucune donnée tabulaire exploitable dans le fichier.");
            }

            return sheets;
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Impossible de lire le fichier. Formats supportés : {AcceptedExtensionsHint}. Détail : {ex.Message}",
                ex);
        }
    }

    public static IReadOnlyList<(string SheetName, string Csv)> ToCsvSheets(byte[] fileContent, string? fileName = null)
    {
        var sheets = Load(fileContent, fileName);
        var list = new List<(string, string)>();
        foreach (var sheet in sheets)
        {
            var sb = new StringBuilder();
            foreach (var row in sheet.Rows)
            {
                if (row.All(string.IsNullOrWhiteSpace))
                {
                    continue;
                }

                sb.AppendLine(string.Join(';', row.Select(EscapeCsv)));
            }

            if (sb.Length > 0)
            {
                list.Add((sheet.Name, sb.ToString()));
            }
        }

        if (list.Count == 0)
        {
            throw new InvalidOperationException("Toutes les feuilles sont vides.");
        }

        return list;
    }

    private static List<Sheet> DetectAndLoad(byte[] content, string ext, string? fileName)
    {
        if (ext is ".gdoc" or ".gsheet" or ".gslides")
        {
            RejectGoogleShortcut(ext, fileName);
        }

        if (ext is ".csv" or ".tsv" or ".txt")
        {
            return [LoadDelimited(content, ext == ".tsv" ? '\t' : DetectDelimiter(content))];
        }

        if (ext is ".docx")
        {
            return LoadDocx(content);
        }

        if (ext is ".pdf")
        {
            return LoadPdf(content);
        }

        if (ext is ".doc")
        {
            throw new InvalidOperationException(
                "Le format Word historique (.doc) n'est pas supporté. Enregistrez ou exportez en .docx, .pdf ou Excel.");
        }

        if (ext is ".xlsx" or ".xls" or ".xlsm")
        {
            return LoadExcel(content);
        }

        // Détection par signature si extension absente / atypique.
        if (LooksLikePdf(content))
        {
            return LoadPdf(content);
        }

        if (LooksLikeZipPackage(content))
        {
            // xlsx / docx / ods partagent la signature ZIP.
            try
            {
                return LoadExcel(content);
            }
            catch
            {
                return LoadDocx(content);
            }
        }

        if (LooksLikeOleCompound(content))
        {
            try
            {
                return LoadExcel(content);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    "Fichier binaire Office détecté mais illisible. Utilisez .xlsx, .xls ou .docx. " + ex.Message,
                    ex);
            }
        }

        // Texte brut / CSV sans extension claire.
        if (LooksLikeText(content))
        {
            return [LoadDelimited(content, DetectDelimiter(content))];
        }

        // Dernier recours : tenter Excel.
        return LoadExcel(content);
    }

    private static List<Sheet> LoadExcel(byte[] content)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        using var stream = new MemoryStream(content);
        using var reader = ExcelReaderFactory.CreateReader(stream);
        var dataSet = reader.AsDataSet(new ExcelDataSetConfiguration { UseColumnDataType = false });
        if (dataSet.Tables.Count == 0)
        {
            throw new InvalidOperationException("Le classeur ne contient aucune feuille.");
        }

        var sheets = new List<Sheet>();
        foreach (DataTable table in dataSet.Tables)
        {
            var rows = new List<IReadOnlyList<string>>();
            var columnCount = table.Columns.Count;
            foreach (DataRow dataRow in table.Rows)
            {
                var values = new List<string>(columnCount);
                for (var c = 0; c < columnCount; c++)
                {
                    values.Add(CellText(dataRow[c], includeTime: true));
                }

                rows.Add(values);
            }

            sheets.Add(new Sheet(string.IsNullOrWhiteSpace(table.TableName) ? $"Feuille{sheets.Count + 1}" : table.TableName, rows, columnCount));
        }

        return sheets;
    }

    private static Sheet LoadDelimited(byte[] content, char delimiter)
    {
        var text = DecodeText(content);
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n', StringSplitOptions.None);

        var rows = new List<IReadOnlyList<string>>();
        var maxCols = 0;
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var cells = SplitDelimitedLine(line, delimiter);
            maxCols = Math.Max(maxCols, cells.Count);
            rows.Add(cells);
        }

        if (rows.Count == 0)
        {
            throw new InvalidOperationException("Fichier texte/CSV vide.");
        }

        // Normaliser la largeur des colonnes.
        for (var i = 0; i < rows.Count; i++)
        {
            if (rows[i].Count >= maxCols)
            {
                continue;
            }

            var padded = rows[i].ToList();
            while (padded.Count < maxCols)
            {
                padded.Add(string.Empty);
            }

            rows[i] = padded;
        }

        return new Sheet("Données", rows, maxCols);
    }

    private static List<Sheet> LoadDocx(byte[] content)
    {
        using var stream = new MemoryStream(content);
        using var document = WordprocessingDocument.Open(stream, false);
        var body = document.MainDocumentPart?.Document?.Body
            ?? throw new InvalidOperationException("Document Word (.docx) invalide ou vide.");

        var sheets = new List<Sheet>();
        var tableIndex = 0;
        foreach (var table in body.Elements<Table>())
        {
            tableIndex++;
            var rows = new List<IReadOnlyList<string>>();
            var maxCols = 0;
            foreach (var row in table.Elements<TableRow>())
            {
                var cells = row.Elements<TableCell>()
                    .Select(cell => NormalizeWhitespace(cell.InnerText))
                    .ToList();
                if (cells.All(string.IsNullOrWhiteSpace))
                {
                    continue;
                }

                maxCols = Math.Max(maxCols, cells.Count);
                rows.Add(cells);
            }

            if (rows.Count == 0)
            {
                continue;
            }

            for (var i = 0; i < rows.Count; i++)
            {
                if (rows[i].Count >= maxCols)
                {
                    continue;
                }

                var padded = rows[i].ToList();
                while (padded.Count < maxCols)
                {
                    padded.Add(string.Empty);
                }

                rows[i] = padded;
            }

            sheets.Add(new Sheet(tableIndex == 1 ? "Tableau1" : $"Tableau{tableIndex}", rows, maxCols));
        }

        if (sheets.Count > 0)
        {
            return sheets;
        }

        // Pas de tableau : une colonne = paragraphes (utile pour un export Docs simple).
        var paragraphs = body.Elements<Paragraph>()
            .Select(p => NormalizeWhitespace(p.InnerText))
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => (IReadOnlyList<string>)new List<string> { t })
            .ToList();

        if (paragraphs.Count == 0)
        {
            throw new InvalidOperationException(
                "Le document Word ne contient ni tableau ni texte exploitable. Ajoutez un tableau, ou exportez en Excel/CSV.");
        }

        return [new Sheet("Texte", paragraphs, 1)];
    }

    private static List<Sheet> LoadPdf(byte[] content)
    {
        if (PromodPurchaseOrderPdfParser.TryParse(content, out var promodOrder, out _)
            && promodOrder is not null)
        {
            return [PromodPurchaseOrderPdfParser.ToCanonicalSheet(promodOrder)];
        }

        using var stream = new MemoryStream(content);
        using var document = PdfDocument.Open(stream);
        var allRows = new List<IReadOnlyList<string>>();
        var maxCols = 0;

        foreach (var page in document.GetPages())
        {
            var words = page.GetWords().ToList();
            if (words.Count == 0)
            {
                continue;
            }

            // Grouper les mots par ligne (Y proche), puis colonnes (X).
            var lineGroups = GroupWordsByLine(words);
            foreach (var line in lineGroups)
            {
                var cells = SplitLineIntoCells(line);
                if (cells.Count == 0 || cells.All(string.IsNullOrWhiteSpace))
                {
                    continue;
                }

                maxCols = Math.Max(maxCols, cells.Count);
                allRows.Add(cells);
            }
        }

        if (allRows.Count == 0)
        {
            throw new InvalidOperationException(
                "Aucune donnée extractible du PDF (scan image ou PDF vide). Exportez en Excel/CSV si possible.");
        }

        for (var i = 0; i < allRows.Count; i++)
        {
            if (allRows[i].Count >= maxCols)
            {
                continue;
            }

            var padded = allRows[i].ToList();
            while (padded.Count < maxCols)
            {
                padded.Add(string.Empty);
            }

            allRows[i] = padded;
        }

        return [new Sheet("PDF", allRows, maxCols)];
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

    private static List<string> SplitLineIntoCells(IReadOnlyList<Word> lineWords)
    {
        if (lineWords.Count == 0)
        {
            return [];
        }

        // Si peu d'espace entre mots → même cellule ; grand gap → nouvelle colonne.
        var gaps = new List<double>();
        for (var i = 1; i < lineWords.Count; i++)
        {
            gaps.Add(lineWords[i].BoundingBox.Left - lineWords[i - 1].BoundingBox.Right);
        }

        var medianGap = gaps.Count == 0
            ? 0
            : gaps.OrderBy(g => g).ElementAt(gaps.Count / 2);
        var columnBreak = Math.Max(12.0, medianGap * 2.5);

        var cells = new List<string>();
        var current = new StringBuilder(lineWords[0].Text);
        for (var i = 1; i < lineWords.Count; i++)
        {
            var gap = lineWords[i].BoundingBox.Left - lineWords[i - 1].BoundingBox.Right;
            if (gap > columnBreak)
            {
                cells.Add(current.ToString().Trim());
                current.Clear();
                current.Append(lineWords[i].Text);
            }
            else
            {
                if (current.Length > 0)
                {
                    current.Append(' ');
                }

                current.Append(lineWords[i].Text);
            }
        }

        cells.Add(current.ToString().Trim());
        return cells;
    }

    private static List<string> SplitDelimitedLine(string line, char delimiter)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;
        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (ch == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }

                continue;
            }

            if (ch == delimiter && !inQuotes)
            {
                result.Add(current.ToString().Trim());
                current.Clear();
                continue;
            }

            current.Append(ch);
        }

        result.Add(current.ToString().Trim());
        return result;
    }

    private static char DetectDelimiter(byte[] content)
    {
        var sample = DecodeText(content.AsSpan(0, Math.Min(content.Length, 4096)).ToArray());
        var firstLines = sample.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Take(5).ToList();
        if (firstLines.Count == 0)
        {
            return ';';
        }

        var candidates = new[] { ';', ',', '\t', '|' };
        return candidates
            .Select(d => (Delimiter: d, Score: firstLines.Sum(l => l.Count(c => c == d))))
            .OrderByDescending(x => x.Score)
            .First().Delimiter;
    }

    private static string DecodeText(byte[] content)
    {
        // UTF-8 BOM
        if (content.Length >= 3 && content[0] == 0xEF && content[1] == 0xBB && content[2] == 0xBF)
        {
            return Encoding.UTF8.GetString(content, 3, content.Length - 3);
        }

        // UTF-16 LE BOM
        if (content.Length >= 2 && content[0] == 0xFF && content[1] == 0xFE)
        {
            return Encoding.Unicode.GetString(content, 2, content.Length - 2);
        }

        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var utf8 = Encoding.UTF8.GetString(content);
        if (!utf8.Contains('\uFFFD', StringComparison.Ordinal))
        {
            return utf8;
        }

        return Encoding.GetEncoding(1252).GetString(content);
    }

    private static string CellText(object? value, bool includeTime) => value switch
    {
        null or DBNull => string.Empty,
        DateTime date when includeTime => date.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
        DateTime date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        double number when Math.Abs(number % 1) < double.Epsilon => number.ToString("0", CultureInfo.InvariantCulture),
        double number => number.ToString("0.######", CultureInfo.InvariantCulture),
        float number => number.ToString("0.######", CultureInfo.InvariantCulture),
        _ => value.ToString()?.Trim() ?? string.Empty
    };

    private static string EscapeCsv(string value)
    {
        if (value.Contains(';', StringComparison.Ordinal)
            || value.Contains('"', StringComparison.Ordinal)
            || value.Contains('\n', StringComparison.Ordinal))
        {
            return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
        }

        return value;
    }

    private static string NormalizeWhitespace(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        return Regex.Replace(text.Trim(), @"\s+", " ");
    }

    private static string NormalizeExtension(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return string.Empty;
        }

        return Path.GetExtension(fileName).ToLowerInvariant();
    }

    private static void RejectGoogleShortcut(string ext, string? fileName)
    {
        if (ext is not (".gdoc" or ".gsheet" or ".gslides"))
        {
            return;
        }

        throw new InvalidOperationException(
            $"« {fileName} » est un raccourci Google Drive, pas le document lui-même. " +
            "Dans Google Docs/Sheets : Fichier → Télécharger → Microsoft Word (.docx), Excel (.xlsx), PDF ou CSV, puis réimportez.");
    }

    private static bool LooksLikePdf(byte[] content)
        => content.Length >= 4
           && content[0] == (byte)'%'
           && content[1] == (byte)'P'
           && content[2] == (byte)'D'
           && content[3] == (byte)'F';

    private static bool LooksLikeZipPackage(byte[] content)
        => content.Length >= 4
           && content[0] == (byte)'P'
           && content[1] == (byte)'K';

    private static bool LooksLikeOleCompound(byte[] content)
        => content.Length >= 4
           && content[0] == 0xD0
           && content[1] == 0xCF
           && content[2] == 0x11
           && content[3] == 0xE0;

    private static bool LooksLikeText(byte[] content)
    {
        var sampleLen = Math.Min(content.Length, 512);
        var control = 0;
        for (var i = 0; i < sampleLen; i++)
        {
            var b = content[i];
            if (b == 0)
            {
                return false;
            }

            if (b < 32 && b is not (9 or 10 or 13))
            {
                control++;
            }
        }

        return control < sampleLen / 20;
    }
}
