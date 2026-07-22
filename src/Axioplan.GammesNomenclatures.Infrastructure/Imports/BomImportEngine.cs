using System.Globalization;
using System.Text;
using Axioplan.GammesNomenclatures.Application.Models;

namespace Axioplan.GammesNomenclatures.Infrastructure.Imports;

public sealed record NormalizedBomSheet(
    IReadOnlyList<IReadOnlyList<string>> Rows,
    int ColumnCount);

public sealed record BomExtractedRow(
    int SourceRowNumber,
    string? Reference,
    string? Designation,
    string? Emploi,
    string? Besoin,
    string? Supplier,
    string ComponentCode,
    string ComponentLabel,
    double Quantity,
    string Unit,
    string Behavior,
    string Status,
    IReadOnlyList<string> RowWarnings);

public static class BomImportEngine
{
    private static readonly StringComparer TextComparer = StringComparer.OrdinalIgnoreCase;

    private static readonly IReadOnlyDictionary<string, string[]> FieldCandidates = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
    {
        [ImportFieldKeys.BomDesignation] =
        [
            "fournitures", "designation", "désignation", "libelle", "libellé", "article", "composant", "matiere", "matière",
            "description", "nom article", "item", "component"
        ],
        [ImportFieldKeys.BomReference] =
        [
            "reference", "référence", "ref", "code", "sku", "article_code", "code article", "ref fournisseur"
        ],
        [ImportFieldKeys.BomUsage] = ["emploi", "usage", "utilisation"],
        [ImportFieldKeys.BomNeed] =
        [
            "besoin", "qte", "qté", "quantite", "quantité", "qty", "quantity", "conso", "consommation"
        ],
        [ImportFieldKeys.BomSupplier] = ["fournisseur", "supplier", "vendor", "fabricant"],
        [ImportFieldKeys.BomUnitPrice] = ["prix", "pu", "prix unitaire", "unit price", "price"],
        [ImportFieldKeys.BomPlacement] =
        [
            "placement", "emplacement", "lg", "lg/e", "longueur", "lg par", "zone", "poste"
        ]
    };

    /// <summary>Colonnes d'en-tête présentes mais non associées à un champ métier (traçabilité, jamais silencieuses).</summary>
    public static IReadOnlyList<string> ListUnmappedHeaders(
        IReadOnlyList<string> headerCells,
        IReadOnlyList<ImportFieldMapping> mappings)
    {
        var mapped = mappings
            .Where(m => m.ColumnIndex is int)
            .Select(m => m.ColumnIndex!.Value)
            .ToHashSet();

        var unknown = new List<string>();
        for (var i = 0; i < headerCells.Count; i++)
        {
            var header = headerCells[i]?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(header))
            {
                continue;
            }

            if (!mapped.Contains(i))
            {
                unknown.Add($"Colonne {i + 1} « {header} » non mappée (conservée en aperçu, ignorée à l'import).");
            }
        }

        return unknown;
    }

    public static NormalizedBomSheet NormalizeSheet(IReadOnlyList<IReadOnlyList<string>> rawRows)
    {
        if (rawRows.Count == 0)
        {
            return new NormalizedBomSheet([], 0);
        }

        var columnCount = rawRows.Max(row => row.Count);
        var rows = new List<IReadOnlyList<string>>(rawRows.Count);
        foreach (var rawRow in rawRows)
        {
            var values = new string[columnCount];
            for (var index = 0; index < columnCount; index++)
            {
                values[index] = index < rawRow.Count ? rawRow[index] ?? string.Empty : string.Empty;
            }

            rows.Add(values);
        }

        columnCount = TrimTrailingEmptyColumns(rows, columnCount);
        return new NormalizedBomSheet(rows, columnCount);
    }

    public static int? DetectHeaderRow(NormalizedBomSheet sheet)
    {
        var bestRow = (int?)null;
        var bestScore = 0;
        for (var rowIndex = 1; rowIndex <= sheet.Rows.Count; rowIndex++)
        {
            var score = ScoreHeaderRow(GetRow(sheet, rowIndex));
            if (score > bestScore)
            {
                bestScore = score;
                bestRow = rowIndex;
            }
        }

        return bestScore >= 4 ? bestRow : null;
    }

    public static string ComputeStructureFingerprint(IReadOnlyList<string> headerRow)
    {
        var labels = headerRow
            .Select(NormalizeHeader)
            .Where(label => !string.IsNullOrWhiteSpace(label))
            .ToArray();
        return labels.Length == 0 ? string.Empty : string.Join("|", labels);
    }

    public static IReadOnlyList<ImportFieldMapping> DetectColumnMappings(
        NormalizedBomSheet sheet,
        int headerRowIndex,
        IReadOnlyDictionary<string, int?>? preferredColumns = null)
    {
        var mappings = new List<ImportFieldMapping>();
        var usedColumns = new HashSet<int>();

        foreach (var (fieldKey, candidates) in FieldCandidates)
        {
            int? preferredColumn = null;
            preferredColumns?.TryGetValue(fieldKey, out preferredColumn);
            var mapping = BuildBestMapping(sheet, headerRowIndex, fieldKey, LabelFor(fieldKey), candidates, usedColumns, preferredColumn);
            if (mapping.ColumnIndex is int columnIndex)
            {
                usedColumns.Add(columnIndex);
            }

            mappings.Add(mapping);
        }

        return mappings;
    }

    public static IReadOnlyList<ImportPreviewRow> BuildFullPreview(NormalizedBomSheet sheet)
        => sheet.Rows
            .Select((row, index) => new ImportPreviewRow(index + 1, row))
            .ToList();

    public static IReadOnlyList<BomExtractedRow> ExtractRows(
        NormalizedBomSheet sheet,
        int headerRow,
        int dataStartRow,
        IReadOnlyList<ImportFieldMapping> mappings,
        ICollection<string> warnings)
    {
        var preparedSheet = ApplyDataRegionFill(sheet, headerRow, dataStartRow, mappings);
        var headerCells = GetRow(preparedSheet, headerRow);
        var rows = new List<BomExtractedRow>();
        var emptyStreak = 0;

        for (var rowIndex = dataStartRow; rowIndex <= preparedSheet.Rows.Count; rowIndex++)
        {
            var rawRow = GetRow(sheet, rowIndex);
            if (IsDecorativeRow(rawRow, rowIndex, headerRow, headerCells))
            {
                continue;
            }

            var row = GetRow(preparedSheet, rowIndex);

            var reference = GetMappedValue(mappings, ImportFieldKeys.BomReference, row);
            var designation = GetMappedValue(mappings, ImportFieldKeys.BomDesignation, row);
            var emploi = GetMappedValue(mappings, ImportFieldKeys.BomUsage, row);
            var besoin = GetMappedValue(mappings, ImportFieldKeys.BomNeed, row);
            var supplier = GetMappedValue(mappings, ImportFieldKeys.BomSupplier, row);

            if (IsEffectivelyEmpty(reference, designation, emploi, besoin, supplier))
            {
                emptyStreak++;
                if (emptyStreak >= 5)
                {
                    break;
                }

                continue;
            }

            emptyStreak = 0;
            var rowWarnings = new List<string>();
            var componentLabel = ResolveComponentLabel(designation, reference, rowIndex, rowWarnings);
            var behavior = "FIXED";
            var status = "OK";

            if (!TryResolveBomQuantity(componentLabel, besoin, emploi, out var quantity, out var unit, out var quantityWarning, out var requiresConfirmation))
            {
                quantity = 1.0;
                unit = "PIECE";
                behavior = "COPY_TO_VALIDATE";
                requiresConfirmation = true;
                quantityWarning = $"quantite non interpretable, hypothese importee a 1 PIECE (Besoin='{besoin}', Emploi='{emploi}')";
            }

            if (!string.IsNullOrWhiteSpace(quantityWarning))
            {
                rowWarnings.Add(quantityWarning);
                warnings.Add($"Ligne BOM {rowIndex} : {quantityWarning}");
            }

            var code = NormalizeArticleCode(reference);
            if (string.IsNullOrWhiteSpace(code))
            {
                code = NormalizeArticleCode(componentLabel);
                if (string.IsNullOrWhiteSpace(code))
                {
                    code = $"LIGNE-{rowIndex}";
                }

                rowWarnings.Add("reference composant absente, code technique derive.");
                warnings.Add($"Ligne BOM {rowIndex} : reference composant absente, code technique derive.");
            }

            if (requiresConfirmation)
            {
                behavior = "COPY_TO_VALIDATE";
                status = "A_CONFIRMER";
            }
            else if (ShouldTreatAsVariableMaterial(componentLabel, unit))
            {
                behavior = "CALCULATED";
            }

            rows.Add(new BomExtractedRow(
                rowIndex,
                reference,
                designation,
                emploi,
                besoin,
                supplier,
                code,
                componentLabel,
                quantity,
                unit,
                behavior,
                status,
                rowWarnings));
        }

        return rows;
    }

    public static IReadOnlyList<BomExtractedRow> ToPreviewRows(IReadOnlyList<BomExtractedRow> rows) => rows;

    private static NormalizedBomSheet ApplyDataRegionFill(
        NormalizedBomSheet sheet,
        int headerRow,
        int dataStartRow,
        IReadOnlyList<ImportFieldMapping> mappings)
    {
        var rows = sheet.Rows.Select(row => row.ToArray()).ToList();
        var fillColumns = new[]
            {
                ResolveColumnIndex(mappings, ImportFieldKeys.BomDesignation),
                ResolveColumnIndex(mappings, ImportFieldKeys.BomReference),
                ResolveColumnIndex(mappings, ImportFieldKeys.BomSupplier)
            }
            .Where(index => index is not null)
            .Select(index => index!.Value)
            .Distinct()
            .ToArray();

        for (var rowIndex = dataStartRow; rowIndex <= rows.Count; rowIndex++)
        {
            var row = rows[rowIndex - 1];
            var rawHasContent = fillColumns.Any(columnIndex => !string.IsNullOrWhiteSpace(row[columnIndex]))
                || !string.IsNullOrWhiteSpace(GetMappedValue(mappings, ImportFieldKeys.BomUsage, row))
                || !string.IsNullOrWhiteSpace(GetMappedValue(mappings, ImportFieldKeys.BomNeed, row));
            if (!rawHasContent)
            {
                continue;
            }

            foreach (var columnIndex in fillColumns)
            {
                if (rowIndex == dataStartRow || !string.IsNullOrWhiteSpace(row[columnIndex]))
                {
                    continue;
                }

                var previous = rows[rowIndex - 2][columnIndex];
                if (!string.IsNullOrWhiteSpace(previous))
                {
                    row[columnIndex] = previous;
                }
            }
        }

        return new NormalizedBomSheet(rows, sheet.ColumnCount);
    }

    private static bool IsDecorativeRow(
        IReadOnlyList<string> row,
        int rowNumber,
        int headerRow,
        IReadOnlyList<string> headerCells)
    {
        if (rowNumber < headerRow)
        {
            return true;
        }

        if (rowNumber == headerRow)
        {
            return false;
        }

        if (IsRowEmpty(row))
        {
            return true;
        }

        if (IsHeaderDuplicate(row, headerCells))
        {
            return true;
        }

        var nonEmpty = row.Where(cell => !string.IsNullOrWhiteSpace(cell)).ToArray();
        if (nonEmpty.Length == 1)
        {
            var token = NormalizeHeader(nonEmpty[0]);
            if (token is "COMMENTAIRES" or "FICHE NOMENCLATURE" or "TOTAL")
            {
                return true;
            }

            if (double.TryParse(nonEmpty[0].Replace(",", "."), NumberStyles.Float, CultureInfo.InvariantCulture, out _))
            {
                return true;
            }
        }

        if (nonEmpty.Any(cell => NormalizeHeader(cell) == "COMMENTAIRES"))
        {
            return true;
        }

        if (nonEmpty.Length <= 2 && nonEmpty.Any(cell => NormalizeHeader(cell).StartsWith("CLIENT", StringComparison.Ordinal)))
        {
            return true;
        }

        return false;
    }

    private static bool IsHeaderDuplicate(IReadOnlyList<string> row, IReadOnlyList<string> headerCells)
    {
        var rowLabels = row.Select(NormalizeHeader).Where(label => !string.IsNullOrWhiteSpace(label)).ToArray();
        var headerLabels = headerCells.Select(NormalizeHeader).Where(label => !string.IsNullOrWhiteSpace(label)).ToArray();
        if (rowLabels.Length == 0 || headerLabels.Length == 0)
        {
            return false;
        }

        var overlap = rowLabels.Count(label => headerLabels.Contains(label, StringComparer.OrdinalIgnoreCase));
        return overlap >= Math.Min(3, headerLabels.Length);
    }

    private static ImportFieldMapping BuildBestMapping(
        NormalizedBomSheet sheet,
        int headerRowIndex,
        string fieldKey,
        string label,
        IReadOnlyList<string> candidates,
        ISet<int> usedColumns,
        int? preferredColumn)
    {
        var candidateRows = Enumerable.Range(headerRowIndex - 2, 5)
            .Where(rowIndex => rowIndex > 0 && rowIndex <= sheet.Rows.Count)
            .Distinct()
            .OrderBy(rowIndex => Math.Abs(rowIndex - headerRowIndex))
            .ThenBy(rowIndex => rowIndex);

        var matches = new List<(int RowIndex, int ColumnIndex, string Header, int Score)>();
        foreach (var rowIndex in candidateRows)
        {
            var row = GetRow(sheet, rowIndex);
            for (var columnIndex = 0; columnIndex < row.Count; columnIndex++)
            {
                var header = row[columnIndex];
                var score = ScoreHeaderMatch(header, candidates);
                if (score <= 0)
                {
                    continue;
                }

                if (rowIndex != headerRowIndex)
                {
                    score -= 1;
                }

                if (preferredColumn == columnIndex)
                {
                    score += 2;
                }

                matches.Add((rowIndex, columnIndex, header, score));
            }
        }

        if (matches.Count == 0)
        {
            return new ImportFieldMapping(fieldKey, null, null, false, $"A confirmer : champ '{label}' non detecte.");
        }

        var best = matches
            .OrderByDescending(match => match.Score)
            .ThenBy(match => usedColumns.Contains(match.ColumnIndex) ? 1 : 0)
            .First();

        var ambiguous = matches.Count(match => match.Score == best.Score && match.ColumnIndex != best.ColumnIndex) > 0;
        var notes = ambiguous
            ? "Plusieurs colonnes candidates detectees, verification manuelle recommandee."
            : best.RowIndex == headerRowIndex
                ? null
                : $"Detection voisine ligne {best.RowIndex} - a confirmer.";

        return new ImportFieldMapping(
            fieldKey,
            best.ColumnIndex,
            best.Header,
            !ambiguous && best.RowIndex == headerRowIndex,
            notes);
    }

    private static int ScoreHeaderMatch(string? header, IReadOnlyList<string> candidates)
    {
        var normalized = NormalizeHeader(header);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return 0;
        }

        var best = 0;
        foreach (var candidate in candidates)
        {
            var normalizedCandidate = NormalizeHeader(candidate);
            if (normalized == normalizedCandidate)
            {
                best = Math.Max(best, 5);
            }
            else if (normalized.Contains(normalizedCandidate, StringComparison.OrdinalIgnoreCase))
            {
                best = Math.Max(best, 3);
            }
        }

        return best;
    }

    private static int ScoreHeaderRow(IReadOnlyList<string> row)
    {
        var score = 0;
        foreach (var cell in row)
        {
            var normalized = NormalizeHeader(cell);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                continue;
            }

            if (normalized.Contains("FOURNITURES", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("DESIGNATION", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("LIBELLE", StringComparison.OrdinalIgnoreCase))
            {
                score += 3;
            }
            else if (normalized.Contains("REFERENCE", StringComparison.OrdinalIgnoreCase) || normalized == "REF")
            {
                score += 3;
            }
            else if (normalized.Contains("EMPLOI", StringComparison.OrdinalIgnoreCase)
                     || normalized.Contains("BESOIN", StringComparison.OrdinalIgnoreCase))
            {
                score += 2;
            }
            else if (normalized.Contains("FOURNISSEUR", StringComparison.OrdinalIgnoreCase)
                     || normalized.Contains("PRIX", StringComparison.OrdinalIgnoreCase)
                     || normalized == "PU"
                     || normalized.Contains("PLACEMENT", StringComparison.OrdinalIgnoreCase)
                     || normalized.Contains("EMPLACEMENT", StringComparison.OrdinalIgnoreCase)
                     || normalized.StartsWith("LG", StringComparison.OrdinalIgnoreCase))
            {
                score += 1;
            }
        }

        return score;
    }

    private static bool LooksLikeHeaderToken(string value)
    {
        var normalized = NormalizeHeader(value);
        return normalized.Contains("REFERENCE", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("EMPLOI", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("BESOIN", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("FOURNISSEUR", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("PRIX", StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveComponentLabel(string? designation, string? reference, int rowIndex, ICollection<string> rowWarnings)
    {
        if (!string.IsNullOrWhiteSpace(designation))
        {
            return designation.Trim();
        }

        if (!string.IsNullOrWhiteSpace(reference))
        {
            rowWarnings.Add("designation absente, libelle derive de la reference.");
            return reference.Trim();
        }

        rowWarnings.Add("designation et reference absentes, ligne importee avec libelle technique.");
        return $"Ligne nomenclature {rowIndex}";
    }

    private static bool IsEffectivelyEmpty(params string?[] values)
        => values.All(string.IsNullOrWhiteSpace);

    private static bool IsRowEmpty(IReadOnlyList<string> row)
        => row.All(string.IsNullOrWhiteSpace);

    private static int TrimTrailingEmptyColumns(List<IReadOnlyList<string>> rows, int columnCount)
    {
        while (columnCount > 0 && rows.All(row => string.IsNullOrWhiteSpace(CellAt(row, columnCount - 1))))
        {
            columnCount--;
        }

        return columnCount;
    }

    private static int? ResolveColumnIndex(IReadOnlyList<ImportFieldMapping> mappings, string fieldKey)
        => mappings.FirstOrDefault(item => TextComparer.Equals(item.FieldKey, fieldKey))?.ColumnIndex;

    private static IReadOnlyList<string> GetRow(NormalizedBomSheet sheet, int rowNumber)
        => rowNumber <= 0 || rowNumber > sheet.Rows.Count ? [] : sheet.Rows[rowNumber - 1];

    private static string? GetMappedValue(IReadOnlyList<ImportFieldMapping> mappings, string fieldKey, IReadOnlyList<string> row)
    {
        var mapping = mappings.FirstOrDefault(item => TextComparer.Equals(item.FieldKey, fieldKey));
        return mapping?.ColumnIndex is int index ? CellAt(row, index) : null;
    }

    private static string CellAt(IReadOnlyList<string> row, int index)
        => index >= 0 && index < row.Count ? row[index] : string.Empty;

    private static string LabelFor(string fieldKey) => fieldKey switch
    {
        ImportFieldKeys.BomReference => "Reference composant",
        ImportFieldKeys.BomDesignation => "Designation composant",
        ImportFieldKeys.BomSupplier => "Fournisseur",
        ImportFieldKeys.BomPlacement => "Placement / emplacement",
        ImportFieldKeys.BomUsage => "Emploi",
        ImportFieldKeys.BomNeed => "Besoin",
        ImportFieldKeys.BomUnitPrice => "PU",
        _ => fieldKey
    };

    private static bool TryResolveBomQuantity(
        string componentLabel,
        string? besoin,
        string? emploi,
        out double quantity,
        out string unit,
        out string? warning,
        out bool requiresConfirmation)
    {
        warning = null;
        requiresConfirmation = false;
        if (TryParseDouble(besoin, out quantity) && quantity > 0)
        {
            unit = "PIECE";
            return true;
        }

        if (!string.IsNullOrWhiteSpace(besoin) && !TryParseDouble(besoin, out _))
        {
            warning = $"Besoin non numerique '{besoin}'";
            requiresConfirmation = true;
        }

        if (TryParseQuantityWithUnit(emploi, out quantity, out unit))
        {
            warning = string.IsNullOrWhiteSpace(warning)
                ? $"quantite deduite depuis Emploi='{emploi}'"
                : $"{warning}; quantite deduite depuis Emploi='{emploi}'";
            requiresConfirmation = UnitRequiresConfirmation(unit) || requiresConfirmation;
            return true;
        }

        quantity = 1.0;
        unit = "PIECE";
        warning = string.IsNullOrWhiteSpace(warning)
            ? $"aucune quantite exploitable lue pour '{componentLabel}', hypothese 1 PIECE par article"
            : warning;
        requiresConfirmation = true;
        return true;
    }

    private static bool TryParseQuantityWithUnit(string? rawValue, out double quantity, out string unit)
    {
        quantity = 0;
        unit = "PIECE";
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return false;
        }

        var cleaned = rawValue.Trim().Replace(",", ".");
        var builder = new StringBuilder();
        foreach (var ch in cleaned)
        {
            if (char.IsDigit(ch) || ch is '.' or '-')
            {
                builder.Append(ch);
            }
            else if (builder.Length > 0)
            {
                break;
            }
        }

        if (builder.Length == 0 || !double.TryParse(builder.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out quantity))
        {
            return false;
        }

        var suffix = cleaned.Substring(builder.Length).Trim();
        unit = string.IsNullOrWhiteSpace(suffix) ? "PIECE" : suffix.ToUpperInvariant();
        return quantity > 0;
    }

    private static bool ShouldTreatAsVariableMaterial(string componentLabel, string unit)
    {
        var normalizedLabel = NormalizeHeader(componentLabel);
        var normalizedUnit = NormalizeHeader(unit);
        return normalizedUnit is "KG" or "G" or "GRAMME" or "GRAMMES"
            || normalizedLabel.Contains("FIL", StringComparison.OrdinalIgnoreCase)
            || normalizedLabel.Contains("MATIERE", StringComparison.OrdinalIgnoreCase);
    }

    private static bool UnitRequiresConfirmation(string unit)
    {
        var normalized = NormalizeHeader(unit);
        return normalized is not ("PIECE" or "PCS" or "KG" or "G" or "CM" or "MM" or "M" or "L32");
    }

    private static bool TryParseDouble(string? value, out double result)
    {
        result = 0;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var cleaned = value.Trim().Replace(" ", string.Empty, StringComparison.Ordinal).Replace(",", ".", StringComparison.Ordinal);
        return double.TryParse(cleaned, NumberStyles.Float, CultureInfo.InvariantCulture, out result);
    }

    private static string NormalizeArticleCode(string? rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return string.Empty;
        }

        var value = rawValue.Trim().ToUpperInvariant();
        var chars = value
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '-')
            .ToArray();
        var normalized = new string(chars);
        while (normalized.Contains("--", StringComparison.Ordinal))
        {
            normalized = normalized.Replace("--", "-", StringComparison.Ordinal);
        }

        return normalized.Trim('-');
    }

    public static string NormalizeHeader(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value.Trim().ToUpperInvariant()
            .Replace("É", "E", StringComparison.Ordinal)
            .Replace("È", "E", StringComparison.Ordinal)
            .Replace("Ê", "E", StringComparison.Ordinal)
            .Replace("À", "A", StringComparison.Ordinal)
            .Replace("Ù", "U", StringComparison.Ordinal)
            .Replace("Ç", "C", StringComparison.Ordinal);
    }
}
