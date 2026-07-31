using System.Data;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Axioplan.GammesNomenclatures.Application.Abstractions;
using Axioplan.GammesNomenclatures.Application.Models;
using Axioplan.GammesNomenclatures.Infrastructure.Data;
using Axioplan.GammesNomenclatures.Infrastructure.Imports;
using Axioplan.GammesNomenclatures.Infrastructure.Logging;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace Axioplan.GammesNomenclatures.Infrastructure.Repositories;

public sealed class SqlServerImportRepository(
    IOptions<DatabaseOptions> options,
    SqlApplicationLogger sqlLogger) : IImportRepository
{
    private static readonly StringComparer TextComparer = StringComparer.OrdinalIgnoreCase;

    public async Task<IReadOnlyList<ImportFamilyItem>> GetFamiliesAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT code, label FROM product_families ORDER BY label";

        var items = new List<ImportFamilyItem>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new ImportFamilyItem(reader.GetString(0), reader.GetString(1)));
        }

        return items;
    }

    public Task<WorkbookImportAnalysis> AnalyzeOrderWorkbookAsync(
        byte[] fileContent,
        string fileName,
        CancellationToken cancellationToken = default)
    {
        var workbook = LoadWorkbook(fileContent, fileName);
        var warnings = new List<string>();
        var sheets = workbook
            .Select(sheet => AnalyzeOrderSheet(sheet, warnings))
            .ToList();

        return Task.FromResult<WorkbookImportAnalysis>(new(
            ImportTargets.Order,
            fileName,
            BuildOrderFieldDefinitions(),
            sheets,
            warnings));
    }

    public async Task<WorkbookImportAnalysis> AnalyzeBomWorkbookAsync(
        byte[] fileContent,
        string fileName,
        CancellationToken cancellationToken = default)
    {
        var workbook = LoadWorkbook(fileContent, fileName);
        var warnings = new List<string>();
        var profiles = await LoadMappingProfilesAsync(ImportTargets.Bom, cancellationToken);
        var sheets = workbook
            .Select(sheet => AnalyzeBomSheet(sheet, warnings, profiles))
            .ToList();

        return new WorkbookImportAnalysis(
            ImportTargets.Bom,
            fileName,
            BuildBomFieldDefinitions(),
            sheets,
            warnings);
    }

    public Task<IReadOnlyList<ImportMappingProfile>> GetMappingProfilesAsync(
        string importTarget,
        CancellationToken cancellationToken = default)
        => LoadMappingProfilesAsync(importTarget, cancellationToken);

    public Task<IReadOnlyList<BomExtractedPreviewRow>> PreviewBomImportAsync(
        byte[] fileContent,
        BomImportRequest request,
        CancellationToken cancellationToken = default)
    {
        var workbook = LoadWorkbook(fileContent, request.FileName);
        var sheet = workbook.FirstOrDefault(s => TextComparer.Equals(s.Name, request.SheetName))
            ?? throw new InvalidOperationException($"Feuille introuvable : {request.SheetName}");

        var warnings = new List<string>();
        var preview = BuildBomExtractedPreview(sheet, request, warnings);
        return Task.FromResult<IReadOnlyList<BomExtractedPreviewRow>>(preview);
    }

    public async Task<ImportExecutionResult> ImportOrderAsync(
        byte[] fileContent,
        OrderImportRequest request,
        CancellationToken cancellationToken = default)
    {
        var workbook = LoadWorkbook(fileContent, request.FileName);
        var sheet = workbook.FirstOrDefault(s => TextComparer.Equals(s.Name, request.SheetName))
            ?? throw new InvalidOperationException($"Feuille introuvable : {request.SheetName}");

        var warnings = new List<string>();
        var metadataLabels = GetRow(sheet, request.MetadataLabelRow);
        var metadataValues = GetRow(sheet, request.MetadataValueRow);
        var sizeHeaders = GetRow(sheet, request.SizeHeaderRow);
        var quantityValues = GetRow(sheet, request.QuantityRow);

        if (sizeHeaders.Count == 0 || quantityValues.Count == 0)
        {
            throw new InvalidOperationException("Les lignes de tailles ou de quantites sont introuvables.");
        }

        var mappedCustomer = GetMappedValue(request.Mappings, ImportFieldKeys.OrderCustomer, metadataValues);
        var mappedReference = GetMappedValue(request.Mappings, ImportFieldKeys.OrderReference, metadataValues);
        var mappedArticle = GetMappedValue(request.Mappings, ImportFieldKeys.OrderArticle, metadataValues);
        var mappedDueDate = GetMappedValue(request.Mappings, ImportFieldKeys.OrderDueDate, metadataValues);
        var mappedGauge = GetMappedValue(request.Mappings, ImportFieldKeys.OrderGauge, metadataValues);
        var mappedVersion = GetMappedValue(request.Mappings, ImportFieldKeys.OrderVersion, metadataValues);

        var customerCode = NormalizeCodeOrFallback(mappedCustomer, "CLIENT_IMPORT");
        var orderCode = NormalizeCodeOrFallback(mappedReference, Path.GetFileNameWithoutExtension(request.FileName).ToUpperInvariant());
        var articleCode = NormalizeCodeOrFallback(
            string.IsNullOrWhiteSpace(mappedArticle) ? mappedReference : mappedArticle,
            orderCode);
        var colorValue = string.IsNullOrWhiteSpace(mappedVersion) ? null : mappedVersion.Trim();
        var orderDate = TryParseDate(mappedDueDate);

        if (orderDate is null && !string.IsNullOrWhiteSpace(mappedDueDate))
        {
            warnings.Add($"Date commande non reconnue '{mappedDueDate}'. Valeur ignoree.");
        }

        var sizeColumns = DetectSizeColumns(sizeHeaders, quantityValues);
        if (sizeColumns.Count == 0)
        {
            throw new InvalidOperationException("Aucune colonne taille exploitable n'a ete detectee.");
        }

        await using var connection = OpenConnection();
        await using var dbTransaction = await connection.BeginTransactionAsync(cancellationToken);
        var transaction = (SqlTransaction)dbTransaction;

        try
        {
            var familyInfo = await LoadFamilyInfoAsync(connection, transaction, request.ProductFamilyCode, cancellationToken)
                ?? throw new InvalidOperationException($"Famille produit introuvable : {request.ProductFamilyCode}");

            var customerId = await EnsureCustomerAsync(connection, transaction, customerCode, mappedCustomer, cancellationToken);
            var articleLabel = string.Join(" - ", new[] { articleCode, mappedGauge, colorValue }.Where(v => !string.IsNullOrWhiteSpace(v)));
            var articleId = await EnsureArticleAsync(
                connection,
                transaction,
                familyInfo.ArticleFamilyId,
                articleCode,
                string.IsNullOrWhiteSpace(articleLabel) ? articleCode : articleLabel,
                "FINISHED_GOOD",
                "PIECE",
                customerId,
                cancellationToken);

            int? colorOptionId = null;
            if (!string.IsNullOrWhiteSpace(colorValue))
            {
                colorOptionId = await EnsureAttributeOptionAsync(connection, transaction, "COLOR", colorValue, warnings, cancellationToken);
            }
            else
            {
                warnings.Add("Couleur/version non renseignee dans la commande. Les lignes sont importees sans couleur.");
            }

            var salesOrderId = await UpsertSalesOrderAsync(
                connection,
                transaction,
                orderCode,
                customerCode,
                $"{orderCode} / {articleLabel}",
                orderDate,
                cancellationToken);

            await DeleteSalesOrderLinesAsync(connection, transaction, salesOrderId, cancellationToken);

            var importedRows = 0;
            var lineNo = 1;
            foreach (var sizeColumn in sizeColumns)
            {
                var rawQuantity = CellAt(quantityValues, sizeColumn.ColumnIndex);
                if (!TryParseDouble(rawQuantity, out var quantity) || quantity <= 0)
                {
                    continue;
                }

                var sizeOptionId = await EnsureAttributeOptionAsync(connection, transaction, "SIZE", sizeColumn.Header, warnings, cancellationToken);
                await InsertSalesOrderLineAsync(
                    connection,
                    transaction,
                    salesOrderId,
                    lineNo++,
                    articleId,
                    sizeOptionId,
                    colorOptionId,
                    quantity,
                    familyInfo.ProductFamilyId,
                    orderCode,
                    cancellationToken);
                importedRows++;
            }

            if (importedRows == 0)
            {
                throw new InvalidOperationException("Aucune ligne de commande exploitable n'a ete importee.");
            }

            var latestBomId = await LoadLatestBomIdAsync(connection, transaction, familyInfo.ProductFamilyId, cancellationToken);
            if (latestBomId is not null)
            {
                await UpsertArticleBomAssignmentAsync(connection, transaction, articleId, latestBomId.Value, cancellationToken);
            }
            else
            {
                warnings.Add("Aucune nomenclature importee/active pour cette famille au moment de l'import commande.");
            }

            warnings.Add("Hypothese V1 : le code article produit fini importe est aligne sur la reference commande.");
            warnings.Add("Hypothese V1 : la famille produit est choisie manuellement avant import pour garantir l'integration Simulation/CBN.");

            await EnsureImportTablesAsync(connection, transaction, cancellationToken);
            var sessionId = await InsertImportSessionAsync(
                connection,
                transaction,
                ImportTargets.Order,
                request.FileName,
                request.SheetName,
                request.ProductFamilyCode,
                importedRows,
                cancellationToken);
            await InsertImportWarningsAsync(connection, transaction, sessionId, warnings, cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            await sqlLogger.LogAsync("INFO", "Imports", "Import commande termine", $"{request.FileName}:{request.SheetName}", cancellationToken);

            return new ImportExecutionResult(
                ImportTargets.Order,
                request.FileName,
                request.SheetName,
                request.ProductFamilyCode,
                importedRows,
                $"Commande {orderCode} / article {articleCode} importée avec {importedRows} lignes taille.",
                warnings);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<ImportExecutionResult> ImportBomAsync(
        byte[] fileContent,
        BomImportRequest request,
        CancellationToken cancellationToken = default)
    {
        var workbook = LoadWorkbook(fileContent, request.FileName);
        var sheet = workbook.FirstOrDefault(s => TextComparer.Equals(s.Name, request.SheetName))
            ?? throw new InvalidOperationException($"Feuille introuvable : {request.SheetName}");

        var warnings = new List<string>();
        var dataRows = ExtractBomRows(sheet, request, warnings);
        if (dataRows.Count == 0)
        {
            throw new InvalidOperationException("Aucune ligne nomenclature exploitable n'a ete detectee.");
        }

        await using var connection = OpenConnection();
        await using var dbTransaction = await connection.BeginTransactionAsync(cancellationToken);
        var transaction = (SqlTransaction)dbTransaction;

        try
        {
            var familyInfo = await LoadFamilyInfoAsync(connection, transaction, request.ProductFamilyCode, cancellationToken)
                ?? throw new InvalidOperationException($"Famille produit introuvable : {request.ProductFamilyCode}");

            var componentFamilyId = await EnsureImportedComponentFamilyAsync(connection, transaction, cancellationToken);
            var version = await LoadNextBomVersionAsync(connection, transaction, familyInfo.ProductFamilyId, cancellationToken);
            var bomCode = $"BOM_IMPORT_{request.ProductFamilyCode}_V{version}";
            var bomId = await InsertBomBaseAsync(connection, transaction, familyInfo.ProductFamilyId, bomCode, version, cancellationToken);

            var lineNo = 1;
            foreach (var row in dataRows)
            {
                var articleId = await EnsureArticleAsync(
                    connection,
                    transaction,
                    componentFamilyId,
                    row.ComponentCode,
                    row.ComponentLabel,
                    "COMPONENT",
                    row.Unit,
                    null,
                    cancellationToken);

                await InsertBomLineAsync(
                    connection,
                    transaction,
                    bomId,
                    lineNo++,
                    articleId,
                    row.Quantity,
                    row.Unit,
                    row.Behavior,
                    cancellationToken);
            }

            await AssignLatestBomToFamilyArticlesAsync(connection, transaction, familyInfo.ProductFamilyId, bomId, cancellationToken);

            warnings.Add("Hypothese V1 : la quantite importe priorise la colonne 'Besoin', sinon l'analyse numerique de 'Emploi'.");
            warnings.Add("Hypothese V1 : les composants sans famille connue sont rattaches a la famille technique IMPORTED_COMPONENT.");
            warnings.Add("Hypothese V1 : l'import nomenclature cree une nouvelle version BOM pour la famille cible et la rend utilisable par CBN.");

            await EnsureImportTablesAsync(connection, transaction, cancellationToken);
            var sessionId = await InsertImportSessionAsync(
                connection,
                transaction,
                ImportTargets.Bom,
                request.FileName,
                request.SheetName,
                request.ProductFamilyCode,
                dataRows.Count,
                cancellationToken);
            await InsertImportWarningsAsync(connection, transaction, sessionId, warnings, cancellationToken);

            if (request.SaveMappingProfile)
            {
                var normalized = BomImportEngine.NormalizeSheet(sheet.Rows);
                var headerCells = GetNormalizedRow(normalized, request.HeaderRow);
                var fingerprint = BomImportEngine.ComputeStructureFingerprint(headerCells);
                if (!string.IsNullOrWhiteSpace(fingerprint))
                {
                    await SaveMappingProfileAsync(
                        connection,
                        transaction,
                        ImportTargets.Bom,
                        fingerprint,
                        string.IsNullOrWhiteSpace(request.MappingProfileName)
                            ? $"Profil {request.SheetName}"
                            : request.MappingProfileName!,
                        request.HeaderRow,
                        request.DataStartRow,
                        request.Mappings,
                        cancellationToken);
                }
            }

            await transaction.CommitAsync(cancellationToken);
            await sqlLogger.LogAsync("INFO", "Imports", "Import nomenclature termine", $"{request.FileName}:{request.SheetName}", cancellationToken);

            return new ImportExecutionResult(
                ImportTargets.Bom,
                request.FileName,
                request.SheetName,
                request.ProductFamilyCode,
                dataRows.Count,
                $"Nomenclature importee dans {bomCode} avec {dataRows.Count} lignes composant.",
                warnings);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private static IReadOnlyList<ImportFieldDefinition> BuildOrderFieldDefinitions() =>
    [
        new(ImportFieldKeys.OrderCustomer, "Client", true, "Client du bon de commande (ex. PROMOD)."),
        new(ImportFieldKeys.OrderReference, "N° commande", true, "Numéro de commande (ex. SO26000329)."),
        new(ImportFieldKeys.OrderArticle, "Article / modèle", true, "Code article produit fini (ex. MBERTILLE)."),
        new(ImportFieldKeys.OrderDueDate, "Date mise à disposition", false, "Date cible / délai client."),
        new(ImportFieldKeys.OrderGauge, "Jauge / info", false, "Information complémentaire conservée dans le libellé."),
        new(ImportFieldKeys.OrderVersion, "Couleur", false, "Couleur du modèle (ex. MAUVE).")
    ];

    private static IReadOnlyList<ImportFieldDefinition> BuildBomFieldDefinitions() =>
    [
        new(ImportFieldKeys.BomReference, "Reference composant", false),
        new(ImportFieldKeys.BomDesignation, "Designation composant", true),
        new(ImportFieldKeys.BomSupplier, "Fournisseur", false),
        new(ImportFieldKeys.BomPlacement, "Placement / emplacement", false),
        new(ImportFieldKeys.BomUsage, "Emploi", false, "Utilisee si Besoin est vide mais Emploi porte une valeur numerique."),
        new(ImportFieldKeys.BomNeed, "Besoin", false, "Prioritaire pour la quantite de consommation."),
        new(ImportFieldKeys.BomUnitPrice, "PU", false)
    ];

    private static ImportSheetAnalysis AnalyzeOrderSheet(WorkbookSheet sheet, List<string> warnings)
    {
        var metadataLabelRow = FindFirstMatchingRow(sheet, ["client", "ref", "article", "couleur", "version", "jauge", "delai"]);
        int? metadataValueRow = metadataLabelRow is int labelRow ? labelRow + 1 : null;
        var sizeHeaderRow = FindRowContaining(sheet, "taille")
            ?? FindRowContainingAny(sheet, ["xs", "xxl", "xl"]);
        var quantityRow = FindRowContainingAny(sheet, ["total comde", "total commande", "quantite", "quantité"]);

        // Feuille canonique bon de commande PDF : tailles juste après les valeurs, quantités ensuite.
        if (sheet.Name.Equals("BonDeCommande", StringComparison.OrdinalIgnoreCase))
        {
            metadataLabelRow ??= 1;
            metadataValueRow ??= 2;
            sizeHeaderRow ??= 3;
            quantityRow ??= 4;
            warnings.Add("Bon de commande PDF reconnu (format PROMOD / YBONSO) — champs préremplis automatiquement, modifiables avant import.");
        }

        if (metadataLabelRow is null)
        {
            warnings.Add($"Commande '{sheet.Name}' : ligne metadonnees non detectee automatiquement.");
        }

        if (sizeHeaderRow is null)
        {
            warnings.Add($"Commande '{sheet.Name}' : ligne d'entetes tailles non detectee automatiquement.");
        }

        if (quantityRow is null)
        {
            warnings.Add($"Commande '{sheet.Name}' : ligne total commande non detectee automatiquement.");
        }

        var suggestedMappings = new List<ImportFieldMapping>();
        if (metadataLabelRow is int rowIndex)
        {
            var row = GetRow(sheet, rowIndex);
            suggestedMappings.Add(BuildMapping(ImportFieldKeys.OrderCustomer, "Client", row, ["client"]));
            suggestedMappings.Add(BuildMapping(ImportFieldKeys.OrderReference, "N° commande", row, ["ref commande", "commande", "ref"]));
            suggestedMappings.Add(BuildMapping(ImportFieldKeys.OrderArticle, "Article / modèle", row, ["article", "modele", "modèle"]));
            suggestedMappings.Add(BuildMapping(ImportFieldKeys.OrderDueDate, "Date mise à disposition", row, ["delai", "délai", "date", "disposition"]));
            suggestedMappings.Add(BuildMapping(ImportFieldKeys.OrderGauge, "Jauge / info", row, ["jauge"]));
            suggestedMappings.Add(BuildMapping(ImportFieldKeys.OrderVersion, "Couleur", row, ["couleur", "version"]));
        }

        return new ImportSheetAnalysis(
            sheet.Name,
            sheet.Rows.Count,
            sheet.ColumnCount,
            BuildPreview(sheet),
            [.. new[] { metadataLabelRow, sizeHeaderRow, quantityRow }.Where(v => v is not null).Select(v => v!.Value)],
            suggestedMappings,
            metadataLabelRow,
            metadataValueRow,
            quantityRow,
            SuggestedSizeHeaderRow: sizeHeaderRow);
    }

    private static ImportSheetAnalysis AnalyzeBomSheet(
        WorkbookSheet sheet,
        List<string> warnings,
        IReadOnlyList<ImportMappingProfile> profiles)
    {
        var normalized = BomImportEngine.NormalizeSheet(sheet.Rows);
        var headerRow = BomImportEngine.DetectHeaderRow(normalized);
        if (headerRow is null)
        {
            warnings.Add($"Nomenclature '{sheet.Name}' : entetes colonnes non detectes automatiquement.");
            headerRow = 1;
        }

        var headerCells = GetNormalizedRow(normalized, headerRow.Value);
        var fingerprint = BomImportEngine.ComputeStructureFingerprint(headerCells);
        var matchedProfile = profiles.FirstOrDefault(profile =>
            TextComparer.Equals(profile.StructureFingerprint, fingerprint));

        var preferredColumns = matchedProfile?.Mappings
            .Where(mapping => mapping.ColumnIndex is not null)
            .ToDictionary(mapping => mapping.FieldKey, mapping => mapping.ColumnIndex, StringComparer.OrdinalIgnoreCase);

        var suggestedMappings = matchedProfile?.Mappings.ToList()
            ?? BomImportEngine.DetectColumnMappings(normalized, headerRow.Value, preferredColumns).ToList();

        if (matchedProfile is not null)
        {
            warnings.Add($"Profil de mapping reconnu pour '{sheet.Name}' : {matchedProfile.ProfileName}.");
        }

        foreach (var unknown in BomImportEngine.ListUnmappedHeaders(headerCells, suggestedMappings))
        {
            warnings.Add($"Nomenclature '{sheet.Name}' : {unknown}");
        }

        var mappedRequired = suggestedMappings.Any(m =>
            m.FieldKey == ImportFieldKeys.BomDesignation && m.ColumnIndex is not null);
        if (!mappedRequired)
        {
            warnings.Add($"Nomenclature '{sheet.Name}' : colonne désignation / fournitures non détectée — corrigez le mapping avant import.");
        }

        var dataStartRow = matchedProfile?.DataStartRow ?? headerRow.Value + 1;
        var extractionWarnings = new List<string>();
        var extractedRows = BomImportEngine.ExtractRows(
            normalized,
            headerRow.Value,
            dataStartRow,
            suggestedMappings,
            extractionWarnings);
        warnings.AddRange(extractionWarnings);

        return new ImportSheetAnalysis(
            sheet.Name,
            normalized.Rows.Count,
            normalized.ColumnCount,
            BomImportEngine.BuildFullPreview(normalized),
            [headerRow.Value],
            suggestedMappings,
            SuggestedDataStartRow: dataStartRow,
            StructureFingerprint: string.IsNullOrWhiteSpace(fingerprint) ? null : fingerprint,
            MatchedProfile: matchedProfile,
            ExtractedPreviewRows: ToExtractedPreviewRows(extractedRows));
    }

    private static List<BomImportRow> ExtractBomRows(WorkbookSheet sheet, BomImportRequest request, List<string> warnings)
    {
        var normalized = BomImportEngine.NormalizeSheet(sheet.Rows);
        return BomImportEngine.ExtractRows(
                normalized,
                request.HeaderRow,
                request.DataStartRow,
                request.Mappings,
                warnings)
            .Select(row => new BomImportRow(
                row.ComponentCode,
                row.ComponentLabel,
                row.Quantity,
                row.Unit,
                row.Behavior))
            .ToList();
    }

    private static IReadOnlyList<BomExtractedPreviewRow> BuildBomExtractedPreview(
        WorkbookSheet sheet,
        BomImportRequest request,
        List<string> warnings)
    {
        var normalized = BomImportEngine.NormalizeSheet(sheet.Rows);
        var extractedRows = BomImportEngine.ExtractRows(
            normalized,
            request.HeaderRow,
            request.DataStartRow,
            request.Mappings,
            warnings);
        return ToExtractedPreviewRows(extractedRows);
    }

    private static IReadOnlyList<BomExtractedPreviewRow> ToExtractedPreviewRows(IReadOnlyList<BomExtractedRow> rows)
        => rows.Select(row => new BomExtractedPreviewRow(
            row.SourceRowNumber,
            row.Reference,
            row.Designation,
            row.Emploi,
            row.Besoin,
            row.Supplier,
            row.Quantity,
            row.Unit,
            row.Behavior,
            row.Status,
            row.RowWarnings)).ToList();

    private static IReadOnlyList<string> GetNormalizedRow(NormalizedBomSheet sheet, int rowNumber)
        => rowNumber <= 0 || rowNumber > sheet.Rows.Count ? [] : sheet.Rows[rowNumber - 1];

    private static List<SizeColumn> DetectSizeColumns(IReadOnlyList<string> headers, IReadOnlyList<string> quantities)
    {
        var sizeColumns = new List<SizeColumn>();
        for (var index = 0; index < Math.Max(headers.Count, quantities.Count); index++)
        {
            var header = CellAt(headers, index);
            if (string.IsNullOrWhiteSpace(header))
            {
                continue;
            }

            var normalized = NormalizeHeader(header);
            if (normalized is "TAILLE" or "TOTAL" || normalized.StartsWith("TOTAL"))
            {
                continue;
            }

            if (!TryParseDouble(CellAt(quantities, index), out _))
            {
                continue;
            }

            sizeColumns.Add(new SizeColumn(index, header.Trim()));
        }

        return sizeColumns;
    }

    private static ImportFieldMapping BuildMapping(string key, string label, IReadOnlyList<string> row, IReadOnlyList<string> candidates)
    {
        for (var index = 0; index < row.Count; index++)
        {
            var normalized = NormalizeHeader(row[index]);
            if (candidates.Any(candidate => normalized.Contains(NormalizeHeader(candidate), StringComparison.OrdinalIgnoreCase)))
            {
                return new ImportFieldMapping(key, index, CellAt(row, index), true);
            }
        }

        return new ImportFieldMapping(key, null, null, false, $"A confirmer : champ '{label}' non detecte.");
    }

    private static int? FindFirstMatchingRow(WorkbookSheet sheet, IReadOnlyList<string> markers)
    {
        for (var rowIndex = 1; rowIndex <= sheet.Rows.Count; rowIndex++)
        {
            var row = GetRow(sheet, rowIndex);
            if (markers.Count(marker => row.Any(cell => NormalizeHeader(cell).Contains(NormalizeHeader(marker), StringComparison.OrdinalIgnoreCase))) >= 2)
            {
                return rowIndex;
            }
        }

        return null;
    }

    private static int? FindRowContaining(WorkbookSheet sheet, string token)
        => FindRowContainingAny(sheet, [token]);

    private static int? FindRowContainingAny(WorkbookSheet sheet, IReadOnlyList<string> tokens)
    {
        for (var rowIndex = 1; rowIndex <= sheet.Rows.Count; rowIndex++)
        {
            var row = GetRow(sheet, rowIndex);
            if (row.Any(cell => tokens.Any(token => NormalizeHeader(cell).Contains(NormalizeHeader(token), StringComparison.OrdinalIgnoreCase))))
            {
                return rowIndex;
            }
        }

        return null;
    }

    private static IReadOnlyList<ImportPreviewRow> BuildPreview(WorkbookSheet sheet)
        => sheet.Rows
            .Take(20)
            .Select((row, index) => new ImportPreviewRow(index + 1, row))
            .ToList();

    private static IReadOnlyList<string> GetRow(WorkbookSheet sheet, int rowNumber)
        => rowNumber <= 0 || rowNumber > sheet.Rows.Count ? [] : sheet.Rows[rowNumber - 1];

    private static string? GetMappedValue(IReadOnlyList<ImportFieldMapping> mappings, string fieldKey, IReadOnlyList<string> row)
    {
        var mapping = mappings.FirstOrDefault(item => TextComparer.Equals(item.FieldKey, fieldKey));
        return mapping?.ColumnIndex is int index ? CellAt(row, index) : null;
    }

    private static string CellAt(IReadOnlyList<string> row, int index)
        => index >= 0 && index < row.Count ? row[index] : string.Empty;

    private static string NormalizeCodeOrFallback(string? rawValue, string fallback)
    {
        var normalized = NormalizeArticleCode(rawValue);
        return string.IsNullOrWhiteSpace(normalized) ? fallback : normalized;
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

    private static string NormalizeHeader(string? value)
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

    private static DateOnly? TryParseDate(string? rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return null;
        }

        if (DateOnly.TryParse(rawValue, CultureInfo.GetCultureInfo("fr-FR"), DateTimeStyles.AllowWhiteSpaces, out var date))
        {
            return date;
        }

        if (DateOnly.TryParse(rawValue, out date))
        {
            return date;
        }

        if (double.TryParse(rawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var excelSerial))
        {
            try
            {
                return DateOnly.FromDateTime(DateTime.FromOADate(excelSerial));
            }
            catch
            {
                return null;
            }
        }

        return null;
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

    private static List<WorkbookSheet> LoadWorkbook(byte[] fileContent, string? fileName = null)
    {
        return UniversalWorkbookLoader.Load(fileContent, fileName)
            .Select(sheet => new WorkbookSheet(sheet.Name, sheet.Rows, sheet.ColumnCount))
            .ToList();
    }

    private static string ToCellText(object? value)
    {
        return value switch
        {
            null => string.Empty,
            DBNull => string.Empty,
            DateTime date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            double number when Math.Abs(number % 1) < double.Epsilon => number.ToString("0", CultureInfo.InvariantCulture),
            double number => number.ToString("0.###", CultureInfo.InvariantCulture),
            float number => number.ToString("0.###", CultureInfo.InvariantCulture),
            _ => value.ToString()?.Trim() ?? string.Empty
        };
    }

    private async Task<FamilyInfo?> LoadFamilyInfoAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string familyCode,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT pf.id, af.id, pf.code, pf.label
            FROM product_families pf
            JOIN article_families af ON af.id = pf.article_family_id
            WHERE pf.code = @familyCode
            """;
        command.Parameters.AddWithValue("@familyCode", familyCode);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new FamilyInfo(
            reader.GetInt32(0),
            reader.GetInt32(1),
            reader.GetString(2),
            reader.GetString(3));
    }

    private async Task<int> EnsureCustomerAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string customerCode,
        string? customerLabel,
        CancellationToken cancellationToken)
    {
        var existingId = await LoadScalarIntAsync(
            connection,
            transaction,
            "SELECT id FROM customers WHERE code = @code",
            [new SqlParameter("@code", customerCode)],
            cancellationToken);
        if (existingId is not null)
        {
            return existingId.Value;
        }

        return await InsertAndReturnIdAsync(
            connection,
            transaction,
            """
            INSERT INTO customers (code, label, status)
            OUTPUT INSERTED.id
            VALUES (@code, @label, 'ACTIVE')
            """,
            [
                new SqlParameter("@code", customerCode),
                new SqlParameter("@label", string.IsNullOrWhiteSpace(customerLabel) ? customerCode : customerLabel!.Trim())
            ],
            cancellationToken);
    }

    private async Task<int> EnsureArticleAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int familyId,
        string code,
        string label,
        string articleType,
        string unit,
        int? customerId,
        CancellationToken cancellationToken)
    {
        var existingId = await LoadScalarIntAsync(
            connection,
            transaction,
            "SELECT id FROM articles WHERE code = @code",
            [new SqlParameter("@code", code)],
            cancellationToken);
        if (existingId is not null)
        {
            await using var update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = """
                UPDATE articles
                SET label = @label,
                    default_unit = @unit,
                    customer_id = COALESCE(@customerId, customer_id),
                    creation_mode = 'IMPORTED',
                    source_system = 'EXCEL_IMPORT'
                WHERE id = @id
                """;
            update.Parameters.AddWithValue("@label", label);
            update.Parameters.AddWithValue("@unit", unit);
            update.Parameters.AddWithValue("@customerId", (object?)customerId ?? DBNull.Value);
            update.Parameters.AddWithValue("@id", existingId.Value);
            await update.ExecuteNonQueryAsync(cancellationToken);
            return existingId.Value;
        }

        return await InsertAndReturnIdAsync(
            connection,
            transaction,
            """
            INSERT INTO articles (
                family_id, code, label, article_type, default_unit, status, source_system, creation_mode, customer_id
            )
            OUTPUT INSERTED.id
            VALUES (
                @familyId, @code, @label, @articleType, @unit, 'ACTIVE', 'EXCEL_IMPORT', 'IMPORTED', @customerId
            )
            """,
            [
                new SqlParameter("@familyId", familyId),
                new SqlParameter("@code", code),
                new SqlParameter("@label", label),
                new SqlParameter("@articleType", articleType),
                new SqlParameter("@unit", unit),
                new SqlParameter("@customerId", (object?)customerId ?? DBNull.Value)
            ],
            cancellationToken);
    }

    private async Task<int> EnsureAttributeOptionAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string attributeCode,
        string rawValue,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        var displayValue = rawValue.Trim();
        var normalizedValue = displayValue.ToUpperInvariant();
        var technicalCode = NormalizeArticleCode(displayValue);
        if (string.IsNullOrWhiteSpace(technicalCode))
        {
            technicalCode = normalizedValue.Replace(' ', '-');
        }

        await using var attributeCommand = connection.CreateCommand();
        attributeCommand.Transaction = transaction;
        attributeCommand.CommandText = """
            SELECT ao.id
            FROM attribute_options ao
            JOIN attribute_definitions ad ON ad.id = ao.attribute_id
            WHERE ad.code = @attributeCode AND ao.normalized_value = @normalizedValue
            """;
        attributeCommand.Parameters.AddWithValue("@attributeCode", attributeCode);
        attributeCommand.Parameters.AddWithValue("@normalizedValue", normalizedValue);
        var existingId = await attributeCommand.ExecuteScalarAsync(cancellationToken);
        if (existingId is int id)
        {
            return id;
        }

        var attributeId = await LoadScalarIntAsync(
            connection,
            transaction,
            "SELECT id FROM attribute_definitions WHERE code = @code",
            [new SqlParameter("@code", attributeCode)],
            cancellationToken) ?? throw new InvalidOperationException($"Attribut introuvable : {attributeCode}");

        var optionId = await InsertAndReturnIdAsync(
            connection,
            transaction,
            """
            INSERT INTO attribute_options (attribute_id, display_value, normalized_value, technical_code, status)
            OUTPUT INSERTED.id
            VALUES (@attributeId, @displayValue, @normalizedValue, @technicalCode, 'VALIDATED')
            """,
            [
                new SqlParameter("@attributeId", attributeId),
                new SqlParameter("@displayValue", displayValue),
                new SqlParameter("@normalizedValue", normalizedValue),
                new SqlParameter("@technicalCode", technicalCode)
            ],
            cancellationToken);

        await CreateDefaultCoefficientsForNewOptionAsync(connection, transaction, attributeId, optionId, cancellationToken);
        warnings.Add($"Nouvelle option {attributeCode} creee automatiquement : {displayValue}.");
        return optionId;
    }

    private static async Task CreateDefaultCoefficientsForNewOptionAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int attributeId,
        int optionId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO consumption_coefficients (product_family_id, attribute_id, option_id, coefficient, source_status, status, notes)
            SELECT pf.id, @attributeId, @optionId, 1.0, 'SIMULATED', 'VALIDATED',
                   N'Coefficient cree automatiquement pour nouvelle option.'
            FROM product_families pf
            WHERE NOT EXISTS (
                SELECT 1
                FROM consumption_coefficients cc
                WHERE cc.product_family_id = pf.id
                  AND cc.attribute_id = @attributeId
                  AND cc.option_id = @optionId
            );

            INSERT INTO time_coefficients (product_family_id, attribute_id, option_id, coefficient, source_status, status, notes)
            SELECT pf.id, @attributeId, @optionId, 1.0, 'SIMULATED', 'VALIDATED',
                   N'Coefficient cree automatiquement pour nouvelle option.'
            FROM product_families pf
            WHERE NOT EXISTS (
                SELECT 1
                FROM time_coefficients tc
                WHERE tc.product_family_id = pf.id
                  AND tc.attribute_id = @attributeId
                  AND tc.option_id = @optionId
            );
            """;
        command.Parameters.AddWithValue("@attributeId", attributeId);
        command.Parameters.AddWithValue("@optionId", optionId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<int> UpsertSalesOrderAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string orderCode,
        string customerCode,
        string? label,
        DateOnly? orderDate,
        CancellationToken cancellationToken)
    {
        var existingId = await LoadScalarIntAsync(
            connection,
            transaction,
            "SELECT id FROM sales_orders WHERE code = @code",
            [new SqlParameter("@code", orderCode)],
            cancellationToken);
        if (existingId is int salesOrderId)
        {
            await using var update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = """
                UPDATE sales_orders
                SET customer_code = @customerCode,
                    label = @label,
                    status = 'OPEN',
                    order_date = @orderDate
                WHERE id = @id
                """;
            update.Parameters.AddWithValue("@customerCode", customerCode);
            update.Parameters.AddWithValue("@label", (object?)label ?? DBNull.Value);
            update.Parameters.AddWithValue("@orderDate", orderDate?.ToDateTime(TimeOnly.MinValue) ?? (object)DBNull.Value);
            update.Parameters.AddWithValue("@id", salesOrderId);
            await update.ExecuteNonQueryAsync(cancellationToken);
            return salesOrderId;
        }

        return await InsertAndReturnIdAsync(
            connection,
            transaction,
            """
            INSERT INTO sales_orders (code, customer_code, label, status, order_date)
            OUTPUT INSERTED.id
            VALUES (@code, @customerCode, @label, 'OPEN', @orderDate)
            """,
            [
                new SqlParameter("@code", orderCode),
                new SqlParameter("@customerCode", customerCode),
                new SqlParameter("@label", (object?)label ?? DBNull.Value),
                new SqlParameter("@orderDate", orderDate?.ToDateTime(TimeOnly.MinValue) ?? (object)DBNull.Value)
            ],
            cancellationToken);
    }

    private static async Task DeleteSalesOrderLinesAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int salesOrderId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "DELETE FROM sales_order_lines WHERE sales_order_id = @salesOrderId";
        command.Parameters.AddWithValue("@salesOrderId", salesOrderId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertSalesOrderLineAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int salesOrderId,
        int lineNo,
        int articleId,
        int? sizeOptionId,
        int? colorOptionId,
        double quantity,
        int productFamilyId,
        string externalRef,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO sales_order_lines (
                sales_order_id, line_no, article_id, size_option_id, color_option_id, quantity, unit, product_family_id, external_ref
            )
            VALUES (
                @salesOrderId, @lineNo, @articleId, @sizeOptionId, @colorOptionId, @quantity, 'PIECE', @productFamilyId, @externalRef
            )
            """;
        command.Parameters.AddWithValue("@salesOrderId", salesOrderId);
        command.Parameters.AddWithValue("@lineNo", lineNo);
        command.Parameters.AddWithValue("@articleId", articleId);
        command.Parameters.AddWithValue("@sizeOptionId", (object?)sizeOptionId ?? DBNull.Value);
        command.Parameters.AddWithValue("@colorOptionId", (object?)colorOptionId ?? DBNull.Value);
        command.Parameters.AddWithValue("@quantity", quantity);
        command.Parameters.AddWithValue("@productFamilyId", productFamilyId);
        command.Parameters.AddWithValue("@externalRef", externalRef);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<int?> LoadLatestBomIdAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int productFamilyId,
        CancellationToken cancellationToken)
    {
        return await LoadScalarIntAsync(
            connection,
            transaction,
            """
            SELECT TOP 1 id
            FROM bom_bases
            WHERE product_family_id = @productFamilyId
            ORDER BY version DESC, id DESC
            """,
            [new SqlParameter("@productFamilyId", productFamilyId)],
            cancellationToken);
    }

    private static async Task UpsertArticleBomAssignmentAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int articleId,
        int bomId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            MERGE article_bom_assignments AS target
            USING (SELECT @articleId AS article_id, @bomId AS bom_base_id) AS source
            ON target.article_id = source.article_id
            WHEN MATCHED THEN UPDATE SET bom_base_id = source.bom_base_id
            WHEN NOT MATCHED THEN INSERT (article_id, bom_base_id) VALUES (source.article_id, source.bom_base_id);
            """;
        command.Parameters.AddWithValue("@articleId", articleId);
        command.Parameters.AddWithValue("@bomId", bomId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<int> EnsureImportedComponentFamilyAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        var existingId = await LoadScalarIntAsync(
            connection,
            transaction,
            "SELECT id FROM article_families WHERE code = 'IMPORTED_COMPONENT'",
            [],
            cancellationToken);
        if (existingId is not null)
        {
            return existingId.Value;
        }

        var categoryId = await LoadScalarIntAsync(
            connection,
            transaction,
            "SELECT id FROM article_categories WHERE code = 'MAIN_MATERIAL'",
            [],
            cancellationToken) ?? throw new InvalidOperationException("Categorie article MAIN_MATERIAL introuvable.");

        return await InsertAndReturnIdAsync(
            connection,
            transaction,
            """
            INSERT INTO article_families (category_id, code, label)
            OUTPUT INSERTED.id
            VALUES (@categoryId, 'IMPORTED_COMPONENT', 'Composant importe')
            """,
            [new SqlParameter("@categoryId", categoryId)],
            cancellationToken);
    }

    private async Task<int> LoadNextBomVersionAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int productFamilyId,
        CancellationToken cancellationToken)
    {
        var version = await LoadScalarIntAsync(
            connection,
            transaction,
            "SELECT ISNULL(MAX(version), 0) + 1 FROM bom_bases WHERE product_family_id = @productFamilyId",
            [new SqlParameter("@productFamilyId", productFamilyId)],
            cancellationToken);
        return version ?? 1;
    }

    private static async Task<int> InsertBomBaseAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int productFamilyId,
        string bomCode,
        int version,
        CancellationToken cancellationToken)
    {
        return await InsertAndReturnIdAsync(
            connection,
            transaction,
            """
            INSERT INTO bom_bases (product_family_id, code, version, status)
            OUTPUT INSERTED.id
            VALUES (@productFamilyId, @code, @version, 'VALIDATED')
            """,
            [
                new SqlParameter("@productFamilyId", productFamilyId),
                new SqlParameter("@code", bomCode),
                new SqlParameter("@version", version)
            ],
            cancellationToken);
    }

    private static async Task InsertBomLineAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int bomId,
        int lineNo,
        int componentArticleId,
        double quantity,
        string unit,
        string behavior,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO bom_base_lines (bom_base_id, line_no, component_article_id, quantity_base, unit, loss_rate, behavior)
            VALUES (@bomId, @lineNo, @componentArticleId, @quantity, @unit, 0, @behavior)
            """;
        command.Parameters.AddWithValue("@bomId", bomId);
        command.Parameters.AddWithValue("@lineNo", lineNo);
        command.Parameters.AddWithValue("@componentArticleId", componentArticleId);
        command.Parameters.AddWithValue("@quantity", quantity);
        command.Parameters.AddWithValue("@unit", unit);
        command.Parameters.AddWithValue("@behavior", behavior);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task AssignLatestBomToFamilyArticlesAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int productFamilyId,
        int bomId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            MERGE article_bom_assignments AS target
            USING (
                SELECT a.id AS article_id, @bomId AS bom_base_id
                FROM articles a
                JOIN article_families af ON af.id = a.family_id
                JOIN product_families pf ON pf.article_family_id = af.id
                WHERE pf.id = @productFamilyId AND a.article_type = 'FINISHED_GOOD'
            ) AS source
            ON target.article_id = source.article_id
            WHEN MATCHED THEN UPDATE SET bom_base_id = source.bom_base_id
            WHEN NOT MATCHED THEN INSERT (article_id, bom_base_id) VALUES (source.article_id, source.bom_base_id);
            """;
        command.Parameters.AddWithValue("@bomId", bomId);
        command.Parameters.AddWithValue("@productFamilyId", productFamilyId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<int> InsertImportSessionAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string importTarget,
        string fileName,
        string sheetName,
        string familyCode,
        int importedRows,
        CancellationToken cancellationToken)
    {
        return await InsertAndReturnIdAsync(
            connection,
            transaction,
            """
            INSERT INTO import_sessions (import_target, file_name, sheet_name, product_family_code, imported_row_count, status)
            OUTPUT INSERTED.id
            VALUES (@importTarget, @fileName, @sheetName, @familyCode, @importedRowCount, 'COMPLETED')
            """,
            [
                new SqlParameter("@importTarget", importTarget),
                new SqlParameter("@fileName", fileName),
                new SqlParameter("@sheetName", sheetName),
                new SqlParameter("@familyCode", familyCode),
                new SqlParameter("@importedRowCount", importedRows)
            ],
            cancellationToken);
    }

    private static async Task EnsureImportTablesAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            IF OBJECT_ID(N'dbo.import_sessions', N'U') IS NULL
            BEGIN
                CREATE TABLE import_sessions (
                    id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    import_target NVARCHAR(20) NOT NULL,
                    file_name NVARCHAR(255) NOT NULL,
                    sheet_name NVARCHAR(255) NOT NULL,
                    product_family_code NVARCHAR(50) NOT NULL,
                    imported_row_count INT NOT NULL DEFAULT 0,
                    status NVARCHAR(20) NOT NULL DEFAULT 'COMPLETED',
                    created_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
                );
            END;

            IF OBJECT_ID(N'dbo.import_session_warnings', N'U') IS NULL
            BEGIN
                CREATE TABLE import_session_warnings (
                    id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    import_session_id INT NOT NULL,
                    warning_message NVARCHAR(1000) NOT NULL,
                    created_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
                    FOREIGN KEY (import_session_id) REFERENCES import_sessions(id)
                );
            END;

            IF OBJECT_ID(N'dbo.import_mapping_profiles', N'U') IS NULL
            BEGIN
                CREATE TABLE import_mapping_profiles (
                    id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    import_target NVARCHAR(20) NOT NULL,
                    structure_fingerprint NVARCHAR(300) NOT NULL,
                    profile_name NVARCHAR(255) NOT NULL,
                    header_row INT NOT NULL,
                    data_start_row INT NOT NULL,
                    mappings_json NVARCHAR(MAX) NOT NULL,
                    created_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
                    updated_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
                    CONSTRAINT uq_import_mapping_profiles_target_fingerprint UNIQUE (import_target, structure_fingerprint)
                );
            END;
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<ImportMappingProfile>> LoadMappingProfilesAsync(
        string importTarget,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = OpenConnection();
            await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
            await EnsureImportTablesAsync(connection, transaction, cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT id, import_target, structure_fingerprint, profile_name, header_row, data_start_row, mappings_json
                FROM import_mapping_profiles
                WHERE import_target = @importTarget
                ORDER BY updated_at DESC, profile_name
                """;
            command.Parameters.AddWithValue("@importTarget", importTarget);

            var profiles = new List<ImportMappingProfile>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var mappings = DeserializeMappings(reader.GetString(6));
                profiles.Add(new ImportMappingProfile(
                    reader.GetInt32(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetInt32(4),
                    reader.GetInt32(5),
                    mappings));
            }

            return profiles;
        }
        catch (SqlException)
        {
            return [];
        }
    }

    private static async Task SaveMappingProfileAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string importTarget,
        string structureFingerprint,
        string profileName,
        int headerRow,
        int dataStartRow,
        IReadOnlyList<ImportFieldMapping> mappings,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            MERGE import_mapping_profiles AS target
            USING (SELECT @importTarget AS import_target, @fingerprint AS structure_fingerprint) AS source
            ON target.import_target = source.import_target
               AND target.structure_fingerprint = source.structure_fingerprint
            WHEN MATCHED THEN
                UPDATE SET
                    profile_name = @profileName,
                    header_row = @headerRow,
                    data_start_row = @dataStartRow,
                    mappings_json = @mappingsJson,
                    updated_at = SYSUTCDATETIME()
            WHEN NOT MATCHED THEN
                INSERT (import_target, structure_fingerprint, profile_name, header_row, data_start_row, mappings_json)
                VALUES (@importTarget, @fingerprint, @profileName, @headerRow, @dataStartRow, @mappingsJson);
            """;
        command.Parameters.AddWithValue("@importTarget", importTarget);
        command.Parameters.AddWithValue("@fingerprint", structureFingerprint);
        command.Parameters.AddWithValue("@profileName", profileName);
        command.Parameters.AddWithValue("@headerRow", headerRow);
        command.Parameters.AddWithValue("@dataStartRow", dataStartRow);
        command.Parameters.AddWithValue("@mappingsJson", SerializeMappings(mappings));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string SerializeMappings(IReadOnlyList<ImportFieldMapping> mappings)
        => JsonSerializer.Serialize(mappings);

    private static IReadOnlyList<ImportFieldMapping> DeserializeMappings(string json)
        => JsonSerializer.Deserialize<List<ImportFieldMapping>>(json) ?? [];

    private static async Task InsertImportWarningsAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        int sessionId,
        IReadOnlyList<string> warnings,
        CancellationToken cancellationToken)
    {
        foreach (var warning in warnings.Distinct(TextComparer))
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO import_session_warnings (import_session_id, warning_message)
                VALUES (@sessionId, @warning)
                """;
            command.Parameters.AddWithValue("@sessionId", sessionId);
            command.Parameters.AddWithValue("@warning", warning);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task<int?> LoadScalarIntAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string sql,
        IReadOnlyList<SqlParameter> parameters,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var parameter in parameters)
        {
            command.Parameters.Add(parameter);
        }

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result switch
        {
            int intValue => intValue,
            long longValue => (int)longValue,
            decimal decimalValue => (int)decimalValue,
            _ => null
        };
    }

    private static async Task<int> InsertAndReturnIdAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string sql,
        IReadOnlyList<SqlParameter> parameters,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var parameter in parameters)
        {
            command.Parameters.Add(parameter);
        }

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result switch
        {
            int intValue => intValue,
            decimal decimalValue => (int)decimalValue,
            _ => throw new InvalidOperationException("Impossible de recuperer l'identifiant SQL insere.")
        };
    }

    private SqlConnection OpenConnection()
    {
        var connectionString = options.Value.ConnectionString
            ?? throw new InvalidOperationException("Connection string manquante : Database:ConnectionString");
        var connection = new SqlConnection(connectionString);
        connection.Open();
        return connection;
    }

    private sealed record WorkbookSheet(string Name, List<IReadOnlyList<string>> Rows, int ColumnCount);

    private sealed record SizeColumn(int ColumnIndex, string Header);

    private sealed record FamilyInfo(int ProductFamilyId, int ArticleFamilyId, string Code, string Label);

    private sealed record BomImportRow(string ComponentCode, string ComponentLabel, double Quantity, string Unit, string Behavior);
}
