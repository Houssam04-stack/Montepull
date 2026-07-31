using System.Data;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Axioplan.GammesNomenclatures.Application.MontepullImport;
using Axioplan.GammesNomenclatures.Domain.Aps.Flux;
using Axioplan.GammesNomenclatures.Domain.MontepullImport;
using Axioplan.GammesNomenclatures.Infrastructure.Data;
using Axioplan.GammesNomenclatures.Infrastructure.Imports;
using Axioplan.GammesNomenclatures.Infrastructure.Logging;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace Axioplan.GammesNomenclatures.Infrastructure.Repositories;

/// <summary>
/// Persistance SQL Server de l'import Montepull (staging -> validation -> promotion metier).
/// Reutilise les moteurs purs de Axioplan.GammesNomenclatures.Domain.MontepullImport.
/// </summary>
public sealed class SqlServerMontepullImportRepository(
    IOptions<DatabaseOptions> options,
    SqlApplicationLogger sqlLogger) : IMontepullImportRepository
{
    private static readonly int[] StandardOperations = [10, 20, 30, 40, 50, 60, 70];

    // ================================================================
    // Schema
    // ================================================================

    public async Task EnsureSchemaAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = OpenConnection();
        await using (var command = connection.CreateCommand())
        {
            command.CommandTimeout = 120;
            command.CommandText = SchemaDdl;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var seedConfig = connection.CreateCommand())
        {
            seedConfig.CommandText = """
                IF NOT EXISTS (SELECT 1 FROM mp_dataset_config)
                    INSERT INTO mp_dataset_config (active_dataset) VALUES (N'DEMO');
                """;
            await seedConfig.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var op in StandardOperations)
        {
            var workcenter = op switch
            {
                <= 30 => "CC_TRICOTAGE",
                <= 50 => "CC_TRAITEMENT",
                _ => "CC_REMAILLAGE"
            };
            await using var seedMapping = connection.CreateCommand();
            seedMapping.CommandText = """
                IF NOT EXISTS (SELECT 1 FROM mp_import_mappings WHERE source_operation_code = @src)
                BEGIN
                    INSERT INTO mp_import_mappings
                        (source_operation_code, axioplan_operation_no, workcenter_code, sequence_no, unit, event_nature, is_active, notes)
                    VALUES
                        (@src, @op, @wc, @op, N'PIECE', N'PRODUCTION', 1,
                         N'Mapping Montepull par defaut (ops 10-70 → ressources APS reelles).');
                END
                ELSE
                BEGIN
                    UPDATE mp_import_mappings
                    SET workcenter_code = COALESCE(workcenter_code, @wc)
                    WHERE source_operation_code = @src AND workcenter_code IS NULL;
                END
                """;
            seedMapping.Parameters.AddWithValue("@src", op.ToString(CultureInfo.InvariantCulture));
            seedMapping.Parameters.AddWithValue("@op", op);
            seedMapping.Parameters.AddWithValue("@wc", workcenter);
            await seedMapping.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    // ================================================================
    // Analyse fichier
    // ================================================================

    public Task<MontepullFilePreviewDto> AnalyzeFileAsync(byte[] content, string fileName, CancellationToken cancellationToken = default)
    {
        var sheets = LoadWorkbook(content, fileName);
        var sheetNames = sheets.Select(s => s.Name).ToList();
        var firstHeaders = sheets.FirstOrDefault(s => s.Rows.Count > 0)?.Rows[0] ?? [];
        var kind = MontepullFileDetector.Detect(fileName, sheetNames, firstHeaders);
        var mainSheet = sheets.OrderByDescending(s => s.Rows.Count).FirstOrDefault();
        var headerPreview = mainSheet?.Rows.Take(10).ToList() ?? [];
        var estimatedRows = Math.Max((mainSheet?.Rows.Count ?? 1) - 1, 0);
        var hash = MontepullHash.Sha256Hex(content);

        return Task.FromResult(new MontepullFilePreviewDto(fileName, kind, hash, sheetNames, headerPreview, estimatedRows));
    }

    // ================================================================
    // Staging
    // ================================================================

    public async Task<MontepullImportSummaryDto> StageFilesAsync(
        IReadOnlyList<(byte[] Content, string FileName)> files,
        string? importedBy,
        string origin,
        CancellationToken cancellationToken = default)
    {
        if (files.Count == 0)
        {
            throw new InvalidOperationException("Aucun fichier fourni pour la mise en staging.");
        }

        await EnsureSchemaAsync(cancellationToken);
        await using var connection = OpenConnection();
        await using var dbTransaction = await connection.BeginTransactionAsync(cancellationToken);
        var transaction = (SqlTransaction)dbTransaction;

        try
        {
            var (batchId, _) = await InsertBatchAsync(connection, transaction, origin, importedBy, cancellationToken);
            var knownOps = await LoadKnownOperationsAsync(connection, transaction, cancellationToken);
            var stagedArticles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var attemptedRows = 0L;

            foreach (var file in files)
            {
                var sheets = LoadWorkbook(file.Content, file.FileName);
                var sheetNames = sheets.Select(s => s.Name).ToList();
                var firstHeaders = sheets.FirstOrDefault(s => s.Rows.Count > 0)?.Rows[0] ?? [];
                var kind = MontepullFileDetector.Detect(file.FileName, sheetNames, firstHeaders);
                var fileHash = MontepullHash.Sha256Hex(file.Content);

                var alreadyPromoted = await ScalarBoolAsync(
                    connection, transaction,
                    """
                    SELECT CASE WHEN EXISTS (
                        SELECT 1 FROM mp_import_files f
                        JOIN mp_import_batches b ON b.id = f.batch_id
                        WHERE f.file_hash = @hash AND b.status = N'PROMOTED'
                    ) THEN 1 ELSE 0 END
                    """,
                    [new SqlParameter("@hash", fileHash)],
                    cancellationToken);

                var fileId = await InsertFileAsync(connection, transaction, batchId, file.FileName, kind.ToString(), fileHash, sheetNames, file.Content.Length, cancellationToken);

                if (alreadyPromoted)
                {
                    await InsertAnomalyAsync(
                        connection, transaction, batchId, fileId, null, null, "INFO", "FILE_ALREADY_PROMOTED",
                        $"Le fichier '{file.FileName}' (hash {fileHash[..12]}...) a deja ete promu dans un batch precedent. Remise en staging autorisee ; les upserts metier evitent les doublons.",
                        cancellationToken);
                }

                attemptedRows += kind switch
                {
                    MontepullFileKind.Commandes => await StageCommandesAsync(connection, transaction, batchId, fileId, sheets, stagedArticles, cancellationToken),
                    MontepullFileKind.ListeSuivi => await StageListeSuiviAsync(connection, transaction, batchId, fileId, sheets, knownOps, stagedArticles, cancellationToken),
                    MontepullFileKind.SuiviOperations => await StageSuiviOperationsAsync(connection, transaction, batchId, fileId, sheets, knownOps, stagedArticles, cancellationToken),
                    MontepullFileKind.Doublygilf => await StageDoublygilfAsync(connection, transaction, batchId, fileId, sheets, cancellationToken),
                    MontepullFileKind.NomenclatureDoulbygilf => await StageNomenclatureAsync(connection, transaction, batchId, fileId, sheets, cancellationToken),
                    _ => await StageUnknownAsync(connection, transaction, batchId, fileId, file.FileName, cancellationToken)
                };
            }

            await BuildAggregatesAsync(connection, transaction, batchId, cancellationToken);
            await BuildDurationStatsAsync(connection, transaction, batchId, cancellationToken);

            var duplicatesDetected = await ComputeDuplicatesDetectedAsync(connection, transaction, batchId, attemptedRows, cancellationToken);
            var summary = await BuildSummaryAsync(connection, transaction, batchId, "STAGING", duplicatesDetected, cancellationToken);
            await UpdateBatchSummaryAsync(connection, transaction, batchId, summary, "Staging termine.", cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            await sqlLogger.LogAsync("INFO", "MontepullImport", "Staging termine", $"batch={batchId} fichiers={files.Count}", cancellationToken);
            return summary;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private static async Task<long> StageUnknownAsync(
        SqlConnection connection, SqlTransaction transaction, long batchId, long fileId, string fileName, CancellationToken cancellationToken)
    {
        await InsertAnomalyAsync(connection, transaction, batchId, fileId, null, null, "WARNING", "UNKNOWN_FILE_KIND",
            $"Type de fichier Montepull non reconnu pour '{fileName}'. Aucune donnee mise en staging pour ce fichier.", cancellationToken);
        return 0L;
    }

    private async Task<long> StageCommandesAsync(
        SqlConnection connection, SqlTransaction transaction, long batchId, long fileId,
        List<WorkbookSheet> sheets, HashSet<string> stagedArticles, CancellationToken cancellationToken)
    {
        var sheet = sheets.FirstOrDefault(s => MontepullFileDetector.Normalize(s.Name).Contains("commande", StringComparison.Ordinal))
            ?? sheets.FirstOrDefault();
        if (sheet is null || sheet.Rows.Count == 0)
        {
            return 0L;
        }

        var map = MontepullColumnIndex.MapHeaders(sheet.Rows[0]);
        var importRows = new List<object?[]>();
        var stagingRows = new List<object?[]>();
        var articleRows = new List<object?[]>();

        for (var i = 1; i < sheet.Rows.Count; i++)
        {
            var row = sheet.Rows[i];
            if (row.All(string.IsNullOrWhiteSpace))
            {
                continue;
            }

            var sourceRowNo = i + 1;
            var parsed = MontepullParsers.ParseCommande(row, map, sourceRowNo);
            if (parsed is null)
            {
                continue;
            }

            var status = "STAGED";
            if (parsed.QtyOrdered < 0 || parsed.QtyLaunched < 0 || parsed.QtyProduced < 0)
            {
                status = "WARNING";
                await InsertAnomalyAsync(connection, transaction, batchId, fileId, sheet.Name, sourceRowNo, "WARNING", "NEGATIVE_QTY",
                    $"Quantite negative detectee pour la commande {parsed.OrderCode} / article {parsed.ArticleCode}.", cancellationToken);
            }

            importRows.Add([batchId, fileId, sheet.Name, sourceRowNo, parsed.RowHash, status, null, SerializeRow(row)]);
            stagingRows.Add([
                batchId, fileId, sourceRowNo, parsed.RowHash,
                parsed.OrderCode, parsed.CustomerCode, parsed.ArticleCode, parsed.Designation,
                parsed.QtyOrdered, parsed.QtyLaunched, parsed.SommeOperations, parsed.SommeOp70Commande, parsed.SommeOp70Article,
                parsed.LastOperation, parsed.QtyProduced, parsed.RemainingToLaunch, parsed.RemainingToProduce, parsed.LaunchStatus, status
            ]);

            if (stagedArticles.Add($"{batchId}:{parsed.ArticleCode}"))
            {
                articleRows.Add([batchId, parsed.ArticleCode, parsed.Designation, sheet.Name]);
            }
        }

        await InsertBatchedAsync(connection, transaction, "mp_import_rows",
            ["batch_id", "file_id", "sheet_name", "source_row_no", "row_hash", "status", "error_message", "raw_json"],
            importRows, cancellationToken);

        await InsertBatchedAsync(connection, transaction, "mp_staging_commandes",
            [
                "batch_id", "file_id", "source_row_no", "row_hash",
                "order_code", "customer_code", "article_code", "designation",
                "qty_ordered", "qty_launched", "somme_operations", "somme_op_70_commande", "somme_op_70_article",
                "last_operation", "qty_produced", "remaining_to_launch", "remaining_to_produce", "launch_status", "status"
            ],
            stagingRows, cancellationToken);

        await InsertBatchedAsync(connection, transaction, "mp_staging_articles",
            ["batch_id", "article_code", "designation", "source_file"], articleRows, cancellationToken);

        return importRows.Count;
    }

    private async Task<long> StageListeSuiviAsync(
        SqlConnection connection, SqlTransaction transaction, long batchId, long fileId,
        List<WorkbookSheet> sheets, HashSet<int> knownOps, HashSet<string> stagedArticles, CancellationToken cancellationToken)
    {
        long attempted = 0;
        var detailSheet = sheets.FirstOrDefault(s => MontepullFileDetector.Normalize(s.Name).Contains("detail", StringComparison.Ordinal));
        if (detailSheet is not null)
        {
            attempted += await StageSuiviDetailsAsync(connection, transaction, batchId, fileId, detailSheet, "LISTE", knownOps, stagedArticles, cancellationToken);
        }

        var routeSheet = sheets.FirstOrDefault(s => MontepullFileDetector.Normalize(s.Name).Contains("feuille route", StringComparison.Ordinal));
        if (routeSheet is not null)
        {
            await StageFeuilleRouteAsync(connection, transaction, batchId, routeSheet, cancellationToken);
        }

        var parOpSheet = sheets.FirstOrDefault(s => MontepullFileDetector.Normalize(s.Name).Contains("par operation", StringComparison.Ordinal));
        if (parOpSheet is not null && parOpSheet.Rows.Count > 1)
        {
            await InsertAnomalyAsync(connection, transaction, batchId, fileId, parOpSheet.Name, null, "INFO", "CONTROL_SHEET_INFO",
                $"Feuille de controle 'Par Operation' presente ({parOpSheet.Rows.Count - 1} lignes) - non staggee individuellement, utilisee uniquement pour controle visuel.", cancellationToken);
        }

        return attempted;
    }

    private async Task<long> StageSuiviOperationsAsync(
        SqlConnection connection, SqlTransaction transaction, long batchId, long fileId,
        List<WorkbookSheet> sheets, HashSet<int> knownOps, HashSet<string> stagedArticles, CancellationToken cancellationToken)
    {
        long attempted = 0;
        var detailSheet = sheets.FirstOrDefault(s => MontepullFileDetector.Normalize(s.Name).Contains("detail", StringComparison.Ordinal));
        if (detailSheet is not null)
        {
            attempted += await StageSuiviDetailsAsync(connection, transaction, batchId, fileId, detailSheet, "HIST", knownOps, stagedArticles, cancellationToken);
        }

        var parOfSheet = sheets.FirstOrDefault(s => MontepullFileDetector.Normalize(s.Name).Contains("par of", StringComparison.Ordinal));
        if (parOfSheet is not null && parOfSheet.Rows.Count > 1)
        {
            await InsertAnomalyAsync(connection, transaction, batchId, fileId, parOfSheet.Name, null, "INFO", "CONTROL_SHEET_INFO",
                $"Feuille de controle 'Par OF' presente ({parOfSheet.Rows.Count - 1} lignes) - agregat de controle, non staggee individuellement.", cancellationToken);
        }

        return attempted;
    }

    private async Task<long> StageSuiviDetailsAsync(
        SqlConnection connection, SqlTransaction transaction, long batchId, long fileId,
        WorkbookSheet sheet, string sourceKind, HashSet<int> knownOps, HashSet<string> stagedArticles,
        CancellationToken cancellationToken)
    {
        if (sheet.Rows.Count == 0)
        {
            return 0L;
        }

        var map = MontepullColumnIndex.MapHeaders(sheet.Rows[0]);
        var importRows = new List<object?[]>();
        var stagingRows = new List<object?[]>();
        var articleRows = new List<object?[]>();
        var anomalyCounts = new Dictionary<(string Severity, string Code), (int Count, int? SampleRow, string SampleMessage)>();
        var storeRawJson = sourceKind != "HIST"; // HIST ~73k lignes : hash suffit, pas de raw volumineux
        var flushSize = sourceKind == "HIST" ? 2000 : 500;
        var attemptedRows = 0L;

        void NoteAnomaly(string severity, string code, int sourceRowNo, string message)
        {
            var key = (severity, code);
            if (anomalyCounts.TryGetValue(key, out var existing))
            {
                anomalyCounts[key] = (existing.Count + 1, existing.SampleRow ?? sourceRowNo, existing.SampleMessage);
            }
            else
            {
                anomalyCounts[key] = (1, sourceRowNo, message);
            }
        }

        for (var i = 1; i < sheet.Rows.Count; i++)
        {
            var row = sheet.Rows[i];
            if (row.All(string.IsNullOrWhiteSpace))
            {
                continue;
            }

            var sourceRowNo = i + 1;
            var parsed = MontepullParsers.ParseSuivi(row, map, sourceRowNo, sheet.Name, sourceKind);
            if (parsed is null)
            {
                continue;
            }

            var status = "STAGED";

            if (parsed.AnomalyCode == "DATE_END_BEFORE_START")
            {
                NoteAnomaly("ERROR", "DATE_END_BEFORE_START", sourceRowNo,
                    $"Date fin < date debut pour OF {parsed.OfCode} operation {parsed.OperationNo}.");
                status = "WARNING";
            }
            else if (parsed.AnomalyCode is not null)
            {
                NoteAnomaly("WARNING", parsed.AnomalyCode, sourceRowNo,
                    $"Anomalie de duree ({parsed.AnomalyCode}) pour OF {parsed.OfCode} operation {parsed.OperationNo}.");
                status = "WARNING";
            }

            if (parsed.QtyGood < 0 || parsed.QtyRejected < 0)
            {
                NoteAnomaly("WARNING", "NEGATIVE_QTY", sourceRowNo,
                    $"Quantite negative pour OF {parsed.OfCode} operation {parsed.OperationNo}.");
                status = "WARNING";
            }

            if (parsed.OperationNo is int opNo && !knownOps.Contains(opNo))
            {
                NoteAnomaly("WARNING", "UNKNOWN_OPERATION", sourceRowNo,
                    $"Operation {opNo} hors mapping standard (mp_import_mappings) — ex. OF {parsed.OfCode}.");
                status = "WARNING";
            }

            if (string.IsNullOrWhiteSpace(parsed.ArticleCode))
            {
                NoteAnomaly("INFO", "MISSING_ARTICLE", sourceRowNo, $"Article manquant pour OF {parsed.OfCode}.");
            }

            if (storeRawJson)
            {
                importRows.Add([
                    batchId, fileId, sheet.Name, sourceRowNo, parsed.RowHash, status, null, SerializeRow(row)
                ]);
            }

            stagingRows.Add([
                batchId, fileId, sheet.Name, sourceRowNo, parsed.RowHash, sourceKind,
                parsed.OfCode, parsed.OrderCode, parsed.ArticleCode, parsed.Designation, parsed.OperationNo,
                parsed.ActualStart, parsed.ActualEnd, parsed.QtyGood, parsed.QtyRejected, parsed.OfQuantity,
                parsed.QtyOrdered, parsed.Weight, parsed.StatusText, parsed.Atelier, parsed.PacketNo,
                parsed.CustomerCode, parsed.CustomerName,
                parsed.OrderDate?.ToDateTime(TimeOnly.MinValue), parsed.DeliveryDate?.ToDateTime(TimeOnly.MinValue), parsed.CustomerOrderRef,
                parsed.DurationSeconds, parsed.DurationKind, status
            ]);

            if (!string.IsNullOrWhiteSpace(parsed.ArticleCode) && stagedArticles.Add($"{batchId}:{parsed.ArticleCode}"))
            {
                articleRows.Add([batchId, parsed.ArticleCode, parsed.Designation, sheet.Name]);
            }

            if (stagingRows.Count >= flushSize)
            {
                attemptedRows += await FlushSuiviBatchesAsync(connection, transaction, storeRawJson ? importRows : null, stagingRows, articleRows, cancellationToken);
            }
        }

        attemptedRows += await FlushSuiviBatchesAsync(connection, transaction, storeRawJson ? importRows : null, stagingRows, articleRows, cancellationToken);

        foreach (var ((severity, code), (count, sampleRow, sampleMessage)) in anomalyCounts)
        {
            var message = count == 1
                ? sampleMessage
                : $"{sampleMessage} (x{count} occurrences dans {sheet.Name})";
            await InsertAnomalyAsync(connection, transaction, batchId, fileId, sheet.Name, sampleRow, severity, code, message, cancellationToken);
        }

        return attemptedRows;

        static async Task<long> FlushSuiviBatchesAsync(
            SqlConnection conn, SqlTransaction tx,
            List<object?[]>? importChunk, List<object?[]> stagingChunk, List<object?[]> articleChunk,
            CancellationToken ct)
        {
            var count = stagingChunk.Count;
            if (importChunk is { Count: > 0 })
            {
                await BulkCopyAsync(conn, tx, "mp_import_rows",
                    ["batch_id", "file_id", "sheet_name", "source_row_no", "row_hash", "status", "error_message", "raw_json"],
                    importChunk, ct);
                importChunk.Clear();
            }

            if (stagingChunk.Count > 0)
            {
                await BulkCopyAsync(conn, tx, "mp_staging_suivi_ops",
                    [
                        "batch_id", "file_id", "sheet_name", "source_row_no", "row_hash", "source_kind",
                        "of_code", "order_code", "article_code", "designation", "operation_no",
                        "actual_start", "actual_end", "qty_good", "qty_rejected", "of_quantity",
                        "qty_ordered", "weight_value", "status_text", "atelier", "packet_no",
                        "customer_code", "customer_name", "order_date", "delivery_date", "customer_order_ref",
                        "duration_seconds", "duration_kind", "status"
                    ],
                    stagingChunk, ct);
                stagingChunk.Clear();
            }

            if (articleChunk.Count > 0)
            {
                await BulkCopyAsync(conn, tx, "mp_staging_articles",
                    ["batch_id", "article_code", "designation", "source_file"], articleChunk, ct);
                articleChunk.Clear();
            }

            return count;
        }
    }

    private static async Task StageFeuilleRouteAsync(
        SqlConnection connection, SqlTransaction transaction, long batchId, WorkbookSheet sheet, CancellationToken cancellationToken)
    {
        if (sheet.Rows.Count == 0)
        {
            return;
        }

        var map = MontepullColumnIndex.MapHeaders(sheet.Rows[0]);
        var rows = new List<object?[]>();

        for (var i = 1; i < sheet.Rows.Count; i++)
        {
            var row = sheet.Rows[i];
            if (row.All(string.IsNullOrWhiteSpace))
            {
                continue;
            }

            var ofCode = MontepullColumnIndex.Get(row, map, "N OF", "N° OF", "n_of");
            if (string.IsNullOrWhiteSpace(ofCode))
            {
                continue;
            }

            var articleCode = MontepullColumnIndex.Get(row, map, "Article")?.Trim();

            foreach (var op in StandardOperations)
            {
                if (MontepullColumnIndex.TryGetDouble(row, map, out var qty, $"Op{op}", $"Op {op}"))
                {
                    rows.Add([batchId, ofCode.Trim(), articleCode, op, qty]);
                }
            }
        }

        await InsertBatchedAsync(connection, transaction, "mp_staging_gamme",
            ["batch_id", "of_code", "article_code", "operation_no", "qty_at_op"], rows, cancellationToken);
    }

    // ----------------------------------------------------------------
    // DOUBLYGILF (besoin d'achat / grille tailles) — mise en page fixe
    // ----------------------------------------------------------------

    private async Task<long> StageDoublygilfAsync(
        SqlConnection connection, SqlTransaction transaction, long batchId, long fileId,
        List<WorkbookSheet> sheets, CancellationToken cancellationToken)
    {
        var sheet = sheets.FirstOrDefault(s => !MontepullFileDetector.Normalize(s.Name).Contains("fiche de prix", StringComparison.Ordinal))
            ?? sheets.FirstOrDefault();
        if (sheet is null)
        {
            return 0L;
        }

        var parentRef = CellAt(sheet, 4, 7).Trim(); // G4
        if (string.IsNullOrWhiteSpace(parentRef))
        {
            parentRef = CellAt(sheet, 7, 4).Trim(); // D7
        }

        if (string.IsNullOrWhiteSpace(parentRef))
        {
            await InsertAnomalyAsync(connection, transaction, batchId, fileId, sheet.Name, null, "ERROR", "MISSING_PARENT_REFERENCE",
                "Reference parent (G4/D7) introuvable dans le fichier DOUBLYGILF.", cancellationToken);
            return 0L;
        }

        var rows = new List<object?[]>();

        // Besoin fil (ligne 15) : F15 = coefficient source, L15 = besoin total (calcule).
        var yarnSupplier = CellAt(sheet, 15, 2);
        var yarnComposition = CellAt(sheet, 15, 3);
        var yarnTitrage = CellAt(sheet, 15, 4);
        var yarnColoris = CellAt(sheet, 15, 5);
        if (TryParseDouble(CellAt(sheet, 15, 6), out var yarnCoefficient))
        {
            var yarnLabel = string.IsNullOrWhiteSpace(yarnComposition) ? "Fil" : $"Fil {yarnComposition}";
            var yarnCode = MontepullFileDetector.NormalizeHeader(string.IsNullOrWhiteSpace(yarnComposition) ? "FIL" : yarnComposition).ToUpperInvariant();
            var yarnTotal = TryParseDouble(CellAt(sheet, 15, 12), out var totalNeed) ? totalNeed : (double?)null;
            var yarnRaw = JsonSerializer.Serialize(new { yarnSupplier, yarnComposition, yarnTitrage, yarnColoris, totalNeedL15 = yarnTotal });
            var yarnHash = MontepullHash.RowHash(parentRef, "YARN", yarnCode, yarnCoefficient.ToString("G17", CultureInfo.InvariantCulture));
            rows.Add([batchId, fileId, 15, yarnHash, parentRef, yarnCode, yarnLabel, yarnCoefficient, "KG", yarnSupplier, 0d, false, yarnRaw]);
        }

        // Fournitures (lignes 21 a 32) : F = quantite source (Emploi/consommation), G/L/O/P = calcules Excel (non stockes).
        for (var excelRow = 21; excelRow <= 32; excelRow++)
        {
            var supplier = CellAt(sheet, excelRow, 2);
            var componentLabel = CellAt(sheet, excelRow, 3);
            if (string.IsNullOrWhiteSpace(componentLabel))
            {
                continue;
            }

            var hasQty = TryParseDouble(CellAt(sheet, excelRow, 6), out var qty);
            var dimLabel = CellAt(sheet, excelRow, 4);
            var dimValue = CellAt(sheet, excelRow, 5);
            var componentCode = MontepullFileDetector.NormalizeHeader(componentLabel).ToUpperInvariant();
            var raw = JsonSerializer.Serialize(new { supplier, componentLabel, dimLabel, dimValue, excelRow });
            var rowHash = MontepullHash.RowHash(parentRef, componentCode, excelRow.ToString(CultureInfo.InvariantCulture));
            rows.Add([batchId, fileId, excelRow, rowHash, parentRef, componentCode, componentLabel.Trim(), hasQty ? qty : (object?)null, "PIECE", supplier, 0d, false, raw]);

            if (!hasQty)
            {
                await InsertAnomalyAsync(connection, transaction, batchId, fileId, sheet.Name, excelRow, "INFO", "SUPPLY_QTY_UNREADABLE",
                    $"Quantite fourniture non numerique pour '{componentLabel}' (ligne {excelRow}) — ligne conservee sans quantite.", cancellationToken);
            }
        }

        await InsertBatchedAsync(connection, transaction, "mp_staging_nomenclature",
            [
                "batch_id", "file_id", "source_row_no", "row_hash", "parent_article_code",
                "component_code", "component_label", "quantity_base", "unit", "supplier", "loss_rate", "is_calculated", "raw_json"
            ],
            rows, cancellationToken);

        return rows.Count;
    }

    // ----------------------------------------------------------------
    // Nomenclature DOULBYGILF — recherche de l'entete FOURNITURES
    // ----------------------------------------------------------------

    private async Task<long> StageNomenclatureAsync(
        SqlConnection connection, SqlTransaction transaction, long batchId, long fileId,
        List<WorkbookSheet> sheets, CancellationToken cancellationToken)
    {
        var sheet = sheets.FirstOrDefault(s => s.Rows.Count > 0) ?? sheets.FirstOrDefault();
        if (sheet is null)
        {
            return 0L;
        }

        var parentRef = FindLabeledValue(sheet, "reference") ?? FindLabeledValue(sheet, "ref");
        if (string.IsNullOrWhiteSpace(parentRef))
        {
            await InsertAnomalyAsync(connection, transaction, batchId, fileId, sheet.Name, null, "ERROR", "MISSING_PARENT_REFERENCE",
                "Reference parent introuvable dans la fiche nomenclature.", cancellationToken);
            return 0L;
        }

        var headerRowIndex = -1;
        for (var i = 0; i < sheet.Rows.Count; i++)
        {
            if (sheet.Rows[i].Any(c => MontepullFileDetector.Normalize(c).Contains("fournitures", StringComparison.Ordinal)))
            {
                headerRowIndex = i;
                break;
            }
        }

        if (headerRowIndex < 0)
        {
            await InsertAnomalyAsync(connection, transaction, batchId, fileId, sheet.Name, null, "ERROR", "MISSING_FOURNITURES_HEADER",
                "En-tete 'FOURNITURES' introuvable dans la fiche nomenclature.", cancellationToken);
            return 0L;
        }

        var headers = sheet.Rows[headerRowIndex];
        var map = MontepullColumnIndex.MapHeaders(headers);
        var labelColumnIndex = headers.ToList().FindIndex(c => MontepullFileDetector.Normalize(c).Contains("fournitures", StringComparison.Ordinal));
        if (labelColumnIndex < 0)
        {
            labelColumnIndex = 0;
        }

        var rows = new List<object?[]>();
        for (var i = headerRowIndex + 1; i < sheet.Rows.Count; i++)
        {
            var row = sheet.Rows[i];
            var componentLabel = CellAt(row, labelColumnIndex).Trim();
            var normalizedLabel = MontepullFileDetector.Normalize(componentLabel);
            if (normalizedLabel is "commentaires" or "commentaire")
            {
                break;
            }

            if (string.IsNullOrWhiteSpace(componentLabel))
            {
                continue;
            }

            var reference = MontepullColumnIndex.Get(row, map, "Reference", "Référence");
            var supplier = MontepullColumnIndex.Get(row, map, "Fournisseur");
            var employRaw = MontepullColumnIndex.Get(row, map, "Emploi");
            var quantityBase = TryParseDouble(employRaw, out var emp) ? emp : (double?)null;
            var unit = quantityBase is null && !string.IsNullOrWhiteSpace(employRaw) ? employRaw : null;

            var componentCode = MontepullFileDetector.NormalizeHeader(string.IsNullOrWhiteSpace(reference) ? componentLabel : reference).ToUpperInvariant();
            var sourceRowNo = i + 1;
            var raw = JsonSerializer.Serialize(new { componentLabel, reference, supplier, employRaw });
            var rowHash = MontepullHash.RowHash(parentRef, componentCode, sourceRowNo.ToString(CultureInfo.InvariantCulture));

            rows.Add([batchId, fileId, sourceRowNo, rowHash, parentRef, componentCode, componentLabel, quantityBase, unit, supplier, 0d, false, raw]);
        }

        await InsertBatchedAsync(connection, transaction, "mp_staging_nomenclature",
            [
                "batch_id", "file_id", "source_row_no", "row_hash", "parent_article_code",
                "component_code", "component_label", "quantity_base", "unit", "supplier", "loss_rate", "is_calculated", "raw_json"
            ],
            rows, cancellationToken);

        return rows.Count;
    }

    private static string? FindLabeledValue(WorkbookSheet sheet, string labelToken)
    {
        var normalizedToken = MontepullFileDetector.Normalize(labelToken);
        for (var r = 0; r < sheet.Rows.Count; r++)
        {
            var row = sheet.Rows[r];
            for (var c = 0; c < row.Count; c++)
            {
                if (MontepullFileDetector.Normalize(row[c]).Contains(normalizedToken, StringComparison.Ordinal)
                    && c + 1 < row.Count
                    && !string.IsNullOrWhiteSpace(row[c + 1]))
                {
                    return row[c + 1].Trim();
                }
            }
        }

        return null;
    }

    // ================================================================
    // Agregats & stats de duree
    // ================================================================

    private static async Task BuildAggregatesAsync(SqlConnection connection, SqlTransaction transaction, long batchId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = 600;
        command.CommandText = """
            INSERT INTO mp_of_operation_agg (
                batch_id, of_code, operation_no, qty_good_total, qty_rejected_total,
                first_start, last_end, packet_count, status_agg, remaining_qty)
            SELECT
                @batchId,
                of_code,
                operation_no,
                SUM(qty_good),
                SUM(qty_rejected),
                MIN(actual_start),
                MAX(actual_end),
                COUNT(DISTINCT NULLIF(packet_no, N'')),
                MAX(status_text),
                CASE
                    WHEN MAX(of_quantity) IS NULL THEN NULL
                    ELSE CASE WHEN MAX(of_quantity) - SUM(qty_good) > 0 THEN MAX(of_quantity) - SUM(qty_good) ELSE 0 END
                END
            FROM mp_staging_suivi_ops
            WHERE batch_id = @batchId AND operation_no IS NOT NULL
            GROUP BY of_code, operation_no;
            """;
        command.Parameters.AddWithValue("@batchId", batchId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task BuildDurationStatsAsync(SqlConnection connection, SqlTransaction transaction, long batchId, CancellationToken cancellationToken)
    {
        var byOp = new Dictionary<int, List<double>>();
        var excludedByOp = new Dictionary<int, int>();

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                SELECT operation_no, duration_seconds, duration_kind
                FROM mp_staging_suivi_ops
                WHERE batch_id = @batchId AND operation_no IS NOT NULL AND duration_kind IS NOT NULL
                """;
            command.Parameters.AddWithValue("@batchId", batchId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var op = reader.GetInt32(0);
                var kind = reader.GetString(2);
                if (kind == "PROCESS" && !reader.IsDBNull(1))
                {
                    if (!byOp.TryGetValue(op, out var list))
                    {
                        list = [];
                        byOp[op] = list;
                    }

                    list.Add(reader.GetDouble(1));
                }
                else if (kind is "SCAN" or "INVALID")
                {
                    excludedByOp[op] = excludedByOp.GetValueOrDefault(op) + 1;
                }
            }
        }

        var rows = new List<object?[]>();
        foreach (var op in byOp.Keys.Union(excludedByOp.Keys).Distinct())
        {
            var values = byOp.GetValueOrDefault(op, []);
            var excluded = excludedByOp.GetValueOrDefault(op);
            var stats = MontepullDurationAnalyzer.ComputeStats(values, excluded);
            rows.Add([
                batchId, op, stats.ObservationCount, stats.MeanSeconds, stats.MedianSeconds, stats.MinSeconds, stats.MaxSeconds,
                stats.P25Seconds, stats.P75Seconds, stats.StdDevSeconds, stats.ExcludedAnomalyCount
            ]);
        }

        await InsertBatchedAsync(connection, transaction, "mp_duration_stats",
            ["batch_id", "operation_no", "observation_count", "mean_seconds", "median_seconds", "min_seconds", "max_seconds", "p25_seconds", "p75_seconds", "stddev_seconds", "excluded_anomaly_count"],
            rows, cancellationToken);
    }

    // ================================================================
    // Validation
    // ================================================================

    public async Task<MontepullImportSummaryDto> ValidateBatchAsync(long batchId, CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);
        await using var connection = OpenConnection();
        await using var dbTransaction = await connection.BeginTransactionAsync(cancellationToken);
        var transaction = (SqlTransaction)dbTransaction;

        try
        {
            var batch = await LoadBatchStatusAsync(connection, transaction, batchId, cancellationToken)
                ?? throw new InvalidOperationException($"Batch Montepull {batchId} introuvable.");

            // Nettoyage idempotent des anomalies generees par une validation precedente.
            await using (var cleanup = connection.CreateCommand())
            {
                cleanup.Transaction = transaction;
                cleanup.CommandText = """
                    DELETE FROM mp_import_anomalies
                    WHERE batch_id = @batchId
                      AND code IN (N'MULTI_ARTICLE_OF', N'MULTI_DESIGNATION_ARTICLE', N'QTY_PRODUCED_EXCEEDS_OF', N'DELIVERY_BEFORE_ORDER')
                    """;
                cleanup.Parameters.AddWithValue("@batchId", batchId);
                await cleanup.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (var multiArticle = connection.CreateCommand())
            {
                multiArticle.Transaction = transaction;
                multiArticle.CommandText = """
                    INSERT INTO mp_import_anomalies (batch_id, severity, code, message)
                    SELECT @batchId, N'WARNING', N'MULTI_ARTICLE_OF',
                           N'OF ' + of_code + N' associe a plusieurs articles distincts (' + CAST(COUNT(DISTINCT article_code) AS NVARCHAR(10)) + N').'
                    FROM mp_staging_suivi_ops
                    WHERE batch_id = @batchId AND article_code IS NOT NULL
                    GROUP BY of_code
                    HAVING COUNT(DISTINCT article_code) > 1
                    """;
                multiArticle.Parameters.AddWithValue("@batchId", batchId);
                await multiArticle.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (var multiDesignation = connection.CreateCommand())
            {
                multiDesignation.Transaction = transaction;
                multiDesignation.CommandText = """
                    INSERT INTO mp_import_anomalies (batch_id, severity, code, message)
                    SELECT @batchId, N'WARNING', N'MULTI_DESIGNATION_ARTICLE',
                           N'Article ' + article_code + N' associe a plusieurs designations distinctes (' + CAST(COUNT(DISTINCT designation) AS NVARCHAR(10)) + N').'
                    FROM mp_staging_suivi_ops
                    WHERE batch_id = @batchId AND article_code IS NOT NULL AND designation IS NOT NULL
                    GROUP BY article_code
                    HAVING COUNT(DISTINCT designation) > 1
                    """;
                multiDesignation.Parameters.AddWithValue("@batchId", batchId);
                await multiDesignation.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (var overProduced = connection.CreateCommand())
            {
                overProduced.Transaction = transaction;
                overProduced.CommandText = """
                    INSERT INTO mp_import_anomalies (batch_id, severity, code, message)
                    SELECT @batchId, N'WARNING', N'QTY_PRODUCED_EXCEEDS_OF',
                           N'OF ' + a.of_code + N' operation ' + CAST(a.operation_no AS NVARCHAR(10)) +
                           N' : quantite produite (' + CAST(a.qty_good_total AS NVARCHAR(20)) + N') superieure a la quantite OF (' + CAST(s.of_quantity AS NVARCHAR(20)) + N').'
                    FROM mp_of_operation_agg a
                    JOIN (
                        SELECT of_code, MAX(of_quantity) AS of_quantity
                        FROM mp_staging_suivi_ops
                        WHERE batch_id = @batchId
                        GROUP BY of_code
                    ) s ON s.of_code = a.of_code
                    WHERE a.batch_id = @batchId AND s.of_quantity IS NOT NULL AND a.qty_good_total > s.of_quantity
                    """;
                overProduced.Parameters.AddWithValue("@batchId", batchId);
                await overProduced.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (var lateDelivery = connection.CreateCommand())
            {
                lateDelivery.Transaction = transaction;
                lateDelivery.CommandText = """
                    INSERT INTO mp_import_anomalies (batch_id, severity, code, message)
                    SELECT @batchId, N'WARNING', N'DELIVERY_BEFORE_ORDER',
                           N'OF ' + of_code + N' : date livraison anterieure a la date de commande.'
                    FROM mp_staging_suivi_ops
                    WHERE batch_id = @batchId AND delivery_date IS NOT NULL AND order_date IS NOT NULL AND delivery_date < order_date
                    GROUP BY of_code
                    """;
                lateDelivery.Parameters.AddWithValue("@batchId", batchId);
                await lateDelivery.ExecuteNonQueryAsync(cancellationToken);
            }

            var errorCount = await ScalarIntAsync(connection, transaction,
                "SELECT COUNT(*) FROM mp_import_anomalies WHERE batch_id = @batchId AND severity = N'ERROR'",
                [new SqlParameter("@batchId", batchId)], cancellationToken) ?? 0;

            var newStatus = errorCount == 0 ? "VALIDATED" : "STAGING";
            await using (var updateStatus = connection.CreateCommand())
            {
                updateStatus.Transaction = transaction;
                updateStatus.CommandText = "UPDATE mp_import_batches SET status = @status WHERE id = @batchId";
                updateStatus.Parameters.AddWithValue("@status", newStatus);
                updateStatus.Parameters.AddWithValue("@batchId", batchId);
                await updateStatus.ExecuteNonQueryAsync(cancellationToken);
            }

            var summary = await BuildSummaryAsync(connection, transaction, batchId, newStatus, 0, cancellationToken);
            var message = errorCount == 0
                ? "Validation reussie : aucune anomalie bloquante (ERROR)."
                : $"Validation bloquee : {errorCount} anomalie(s) ERROR a corriger avant promotion.";
            await UpdateBatchSummaryAsync(connection, transaction, batchId, summary, message, cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            await sqlLogger.LogAsync("INFO", "MontepullImport", "Validation batch", $"batch={batchId} statut={newStatus}", cancellationToken);
            return summary;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    // ================================================================
    // Promotion
    // ================================================================

    public async Task<MontepullImportSummaryDto> PromoteBatchAsync(long batchId, CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);
        await using var connection = OpenConnection();
        await using var dbTransaction = await connection.BeginTransactionAsync(cancellationToken);
        var transaction = (SqlTransaction)dbTransaction;

        try
        {
            var status = await LoadBatchStatusAsync(connection, transaction, batchId, cancellationToken)
                ?? throw new InvalidOperationException($"Batch Montepull {batchId} introuvable.");
            if (status is not ("STAGING" or "VALIDATED"))
            {
                throw new InvalidOperationException($"Le batch {batchId} ne peut pas etre promu depuis le statut '{status}'. Statuts autorises : STAGING, VALIDATED.");
            }

            var fgFamilyId = await EnsureFinishedGoodFamilyAsync(connection, transaction, cancellationToken);
            var fgProductFamilyId = await EnsureProductFamilyAsync(connection, transaction, fgFamilyId, cancellationToken);
            var componentFamilyId = await EnsureComponentFamilyAsync(connection, transaction, cancellationToken);
            var workcenterByOp = await LoadWorkcenterMappingsAsync(connection, transaction, cancellationToken);

            await PromoteCommandesAsync(connection, transaction, batchId, fgFamilyId, cancellationToken);
            await PromoteManufacturingOrdersAsync(connection, transaction, batchId, fgFamilyId, workcenterByOp, cancellationToken);
            await PromoteNomenclatureAsync(connection, transaction, batchId, fgFamilyId, fgProductFamilyId, componentFamilyId, cancellationToken);

            await using (var updateStatus = connection.CreateCommand())
            {
                updateStatus.Transaction = transaction;
                updateStatus.CommandText = "UPDATE mp_import_batches SET status = N'PROMOTED' WHERE id = @batchId";
                updateStatus.Parameters.AddWithValue("@batchId", batchId);
                await updateStatus.ExecuteNonQueryAsync(cancellationToken);
            }

            var summary = await BuildSummaryAsync(connection, transaction, batchId, "PROMOTED", 0, cancellationToken);
            await UpdateBatchSummaryAsync(connection, transaction, batchId, summary, "Promotion terminee.", cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            await sqlLogger.LogAsync("INFO", "MontepullImport", "Promotion batch", $"batch={batchId}", cancellationToken);
            return summary;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private static async Task PromoteCommandesAsync(
        SqlConnection connection, SqlTransaction transaction, long batchId, int fgFamilyId, CancellationToken cancellationToken)
    {
        var commandes = new List<(string OrderCode, string? CustomerCode, string ArticleCode, string? Designation, double QtyOrdered, double QtyLaunched, double QtyProduced, int? LastOperation, double RemainingToLaunch, double RemainingToProduce)>();

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                SELECT order_code, customer_code, article_code, designation, qty_ordered, qty_launched, qty_produced, last_operation, remaining_to_launch, remaining_to_produce
                FROM mp_staging_commandes
                WHERE batch_id = @batchId
                """;
            command.Parameters.AddWithValue("@batchId", batchId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                commandes.Add((
                    reader.GetString(0),
                    reader.IsDBNull(1) ? null : reader.GetString(1),
                    reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3),
                    reader.GetDouble(4),
                    reader.GetDouble(5),
                    reader.GetDouble(6),
                    reader.IsDBNull(7) ? null : reader.GetInt32(7),
                    reader.GetDouble(8),
                    reader.GetDouble(9)));
            }
        }

        foreach (var c in commandes)
        {
            int? customerId = null;
            if (!string.IsNullOrWhiteSpace(c.CustomerCode))
            {
                customerId = await EnsureCustomerAsync(connection, transaction, c.CustomerCode, c.CustomerCode, cancellationToken);
            }

            var articleId = await EnsureArticleAsync(connection, transaction, fgFamilyId, c.ArticleCode,
                string.IsNullOrWhiteSpace(c.Designation) ? c.ArticleCode : c.Designation, "FINISHED_GOOD", "PIECE", customerId, cancellationToken);

            var salesOrderId = await UpsertSalesOrderAsync(connection, transaction, c.OrderCode, c.CustomerCode, batchId, cancellationToken);
            await UpsertSalesOrderLineAsync(connection, transaction, salesOrderId, articleId, c.QtyOrdered, c.QtyLaunched, c.QtyProduced,
                c.LastOperation, c.RemainingToLaunch, c.RemainingToProduce, c.OrderCode, batchId, cancellationToken);
        }
    }

    private static async Task PromoteManufacturingOrdersAsync(
        SqlConnection connection, SqlTransaction transaction, long batchId, int fgFamilyId,
        IReadOnlyDictionary<int, int> workcenterByOp, CancellationToken cancellationToken)
    {
        var ofSummaries = new List<OfSummary>();
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                SELECT of_code,
                       MAX(article_code) AS article_code,
                       MAX(designation) AS designation,
                       MAX(order_code) AS order_code,
                       MAX(of_quantity) AS of_quantity,
                       MAX(status_text) AS status_text,
                       MIN(actual_start) AS first_start,
                       MAX(actual_end) AS last_end,
                       MAX(delivery_date) AS delivery_date,
                       MAX(qty_ordered) AS qty_ordered
                FROM mp_staging_suivi_ops
                WHERE batch_id = @batchId
                GROUP BY of_code
                """;
            command.Parameters.AddWithValue("@batchId", batchId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var ofQty = reader.IsDBNull(4) ? (double?)null : reader.GetDouble(4);
                var qtyOrdered = reader.IsDBNull(9) ? (double?)null : reader.GetDouble(9);
                ofSummaries.Add(new OfSummary(
                    reader.GetString(0),
                    reader.IsDBNull(1) ? null : reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3),
                    ofQty ?? qtyOrdered,
                    reader.IsDBNull(5) ? null : reader.GetString(5),
                    reader.IsDBNull(6) ? null : reader.GetDateTime(6),
                    reader.IsDBNull(7) ? null : reader.GetDateTime(7),
                    reader.IsDBNull(8) ? null : reader.GetDateTime(8)));
            }
        }

        foreach (var of in ofSummaries)
        {
            if (string.IsNullOrWhiteSpace(of.ArticleCode))
            {
                await InsertAnomalyAsync(connection, transaction, batchId, null, null, null, "ERROR", "OF_WITHOUT_ARTICLE",
                    $"OF {of.OfCode} sans article identifie - non promu en manufacturing_orders.", cancellationToken);
                continue;
            }

            var lastOp = await ScalarIntAsync(connection, transaction,
                "SELECT MAX(operation_no) FROM mp_of_operation_agg WHERE batch_id = @batchId AND of_code = @of AND qty_good_total > 0",
                [new SqlParameter("@batchId", batchId), new SqlParameter("@of", of.OfCode)], cancellationToken);

            double qtyGood = 0, qtyRejected = 0;
            if (lastOp is int lo)
            {
                await using var lastOpCmd = connection.CreateCommand();
                lastOpCmd.Transaction = transaction;
                lastOpCmd.CommandText = "SELECT qty_good_total, qty_rejected_total FROM mp_of_operation_agg WHERE batch_id = @batchId AND of_code = @of AND operation_no = @op";
                lastOpCmd.Parameters.AddWithValue("@batchId", batchId);
                lastOpCmd.Parameters.AddWithValue("@of", of.OfCode);
                lastOpCmd.Parameters.AddWithValue("@op", lo);
                await using var lastOpReader = await lastOpCmd.ExecuteReaderAsync(cancellationToken);
                if (await lastOpReader.ReadAsync(cancellationToken))
                {
                    qtyGood = lastOpReader.GetDouble(0);
                    qtyRejected = lastOpReader.GetDouble(1);
                }
            }

            var articleId = await EnsureArticleAsync(connection, transaction, fgFamilyId, of.ArticleCode,
                string.IsNullOrWhiteSpace(of.Designation) ? of.ArticleCode : of.Designation, "FINISHED_GOOD", "PIECE", null, cancellationToken);

            var moStatus = MapManufacturingOrderStatus(of.StatusText);
            var moId = await UpsertManufacturingOrderAsync(connection, transaction, of.OfCode, articleId, of.OfQuantity ?? 0,
                moStatus, of.OrderCode, of.FirstStart, of.LastEnd, qtyGood, qtyRejected, lastOp, of.DeliveryDate, batchId, cancellationToken);

            await PromoteManufacturingOrderLinesAsync(connection, transaction, batchId, of.OfCode, moId, workcenterByOp, cancellationToken);
        }

        // Evenements de production : insert bulk unique (idempotent via row_hash).
        await using (var eventsCmd = connection.CreateCommand())
        {
            eventsCmd.Transaction = transaction;
            eventsCmd.CommandTimeout = 600;
            eventsCmd.CommandText = """
                INSERT INTO mo_production_events (
                    manufacturing_order_id, operation_no, packet_no, actual_start, actual_end,
                    planned_start, planned_end, planned_quantity,
                    actual_good_quantity, actual_rejected_quantity, remaining_quantity,
                    duration_seconds, duration_kind, data_source, import_batch_id,
                    row_hash, source_sheet, source_row_no, is_active)
                SELECT
                    mo.id,
                    s.operation_no,
                    s.packet_no,
                    s.actual_start,
                    s.actual_end,
                    NULL, NULL, NULL,
                    s.qty_good,
                    s.qty_rejected,
                    CASE WHEN s.of_quantity IS NULL THEN NULL
                         WHEN s.of_quantity - s.qty_good > 0 THEN s.of_quantity - s.qty_good ELSE 0 END,
                    s.duration_seconds,
                    s.duration_kind,
                    N'MONTEPULL_REAL',
                    @batchId,
                    s.row_hash,
                    s.sheet_name,
                    s.source_row_no,
                    1
                FROM mp_staging_suivi_ops s
                INNER JOIN manufacturing_orders mo ON mo.code = s.of_code
                WHERE s.batch_id = @batchId
                  AND NOT EXISTS (SELECT 1 FROM mo_production_events e WHERE e.row_hash = s.row_hash);
                """;
            eventsCmd.Parameters.AddWithValue("@batchId", batchId);
            await eventsCmd.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task PromoteManufacturingOrderLinesAsync(
        SqlConnection connection, SqlTransaction transaction, long batchId, string ofCode, int moId,
        IReadOnlyDictionary<int, int> workcenterByOp, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT operation_no, qty_good_total FROM mp_of_operation_agg WHERE batch_id = @batchId AND of_code = @of ORDER BY operation_no";
        command.Parameters.AddWithValue("@batchId", batchId);
        command.Parameters.AddWithValue("@of", ofCode);

        var ops = new List<(int Op, double Qty)>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                ops.Add((reader.GetInt32(0), reader.GetDouble(1)));
            }
        }

        foreach (var (op, qty) in ops)
        {
            var exists = await ScalarIntAsync(connection, transaction,
                "SELECT id FROM manufacturing_order_lines WHERE manufacturing_order_id = @moId AND operation_no = @op",
                [new SqlParameter("@moId", moId), new SqlParameter("@op", op)], cancellationToken);
            if (exists is not null)
            {
                continue;
            }

            var nextLineNo = (await ScalarIntAsync(connection, transaction,
                "SELECT COALESCE(MAX(line_no), 0) + 1 FROM manufacturing_order_lines WHERE manufacturing_order_id = @moId",
                [new SqlParameter("@moId", moId)], cancellationToken)) ?? 1;

            await using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO manufacturing_order_lines (manufacturing_order_id, line_no, operation_no, workcenter_id, quantity, unit)
                VALUES (@moId, @lineNo, @op, @workcenterId, @qty, N'PIECE')
                """;
            insert.Parameters.AddWithValue("@moId", moId);
            insert.Parameters.AddWithValue("@lineNo", nextLineNo);
            insert.Parameters.AddWithValue("@op", op);
            insert.Parameters.AddWithValue("@workcenterId", workcenterByOp.TryGetValue(op, out var wcId) ? wcId : (object)DBNull.Value);
            insert.Parameters.AddWithValue("@qty", qty);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task PromoteNomenclatureAsync(
        SqlConnection connection, SqlTransaction transaction, long batchId, int fgFamilyId, int fgProductFamilyId, int componentFamilyId,
        CancellationToken cancellationToken)
    {
        var parents = new List<string>();
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "SELECT DISTINCT parent_article_code FROM mp_staging_nomenclature WHERE batch_id = @batchId";
            command.Parameters.AddWithValue("@batchId", batchId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                parents.Add(reader.GetString(0));
            }
        }

        foreach (var parent in parents)
        {
            var parentArticleId = await EnsureArticleAsync(connection, transaction, fgFamilyId, parent, parent, "FINISHED_GOOD", "PIECE", null, cancellationToken);
            var bomCode = $"BOM_MP_{parent}";
            var bomId = await EnsureBomBaseAsync(connection, transaction, fgProductFamilyId, bomCode, cancellationToken);
            await UpsertArticleBomAssignmentAsync(connection, transaction, parentArticleId, bomId, cancellationToken);

            await using var lineCommand = connection.CreateCommand();
            lineCommand.Transaction = transaction;
            lineCommand.CommandText = """
                SELECT component_code, component_label, quantity_base, unit
                FROM mp_staging_nomenclature
                WHERE batch_id = @batchId AND parent_article_code = @parent AND component_code IS NOT NULL
                """;
            lineCommand.Parameters.AddWithValue("@batchId", batchId);
            lineCommand.Parameters.AddWithValue("@parent", parent);

            var components = new List<(string Code, string? Label, double? Qty, string? Unit)>();
            await using (var reader = await lineCommand.ExecuteReaderAsync(cancellationToken))
            {
                while (await reader.ReadAsync(cancellationToken))
                {
                    components.Add((
                        reader.GetString(0),
                        reader.IsDBNull(1) ? null : reader.GetString(1),
                        reader.IsDBNull(2) ? null : reader.GetDouble(2),
                        reader.IsDBNull(3) ? null : reader.GetString(3)));
                }
            }

            foreach (var component in components)
            {
                var unit = string.IsNullOrWhiteSpace(component.Unit) || component.Unit.Length > 20 ? "PIECE" : component.Unit;
                var componentArticleId = await EnsureArticleAsync(connection, transaction, componentFamilyId, component.Code,
                    string.IsNullOrWhiteSpace(component.Label) ? component.Code : component.Label, "COMPONENT", unit, null, cancellationToken);
                await UpsertBomLineAsync(connection, transaction, bomId, componentArticleId, component.Qty ?? 0, unit, cancellationToken);
            }
        }
    }

    // ================================================================
    // Annulation
    // ================================================================

    public async Task CancelBatchAsync(long batchId, CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);
        await using var connection = OpenConnection();
        await using var dbTransaction = await connection.BeginTransactionAsync(cancellationToken);
        var transaction = (SqlTransaction)dbTransaction;

        try
        {
            await using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = "UPDATE mp_import_batches SET status = N'CANCELLED' WHERE id = @batchId";
                command.Parameters.AddWithValue("@batchId", batchId);
                var affected = await command.ExecuteNonQueryAsync(cancellationToken);
                if (affected == 0)
                {
                    throw new InvalidOperationException($"Batch Montepull {batchId} introuvable.");
                }
            }

            await using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = "UPDATE mo_production_events SET is_active = 0 WHERE import_batch_id = @batchId";
                command.Parameters.AddWithValue("@batchId", batchId);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = "UPDATE manufacturing_orders SET is_active = 0 WHERE import_batch_id = @batchId";
                command.Parameters.AddWithValue("@batchId", batchId);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            await sqlLogger.LogAsync("INFO", "MontepullImport", "Annulation batch", $"batch={batchId}", cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    // ================================================================
    // Lecture / reporting
    // ================================================================

    public async Task<IReadOnlyList<MontepullBatchListItem>> ListBatchesAsync(CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);
        await using var connection = OpenConnection();
        await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT id, batch_uid, status, imported_at, origin, dataset_code,
                       CASE WHEN LEN(summary_json) > 4000 THEN LEFT(summary_json, 4000) ELSE summary_json END
                FROM mp_import_batches
                ORDER BY imported_at DESC
                """;

        var items = new List<MontepullBatchListItem>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new MontepullBatchListItem(
                reader.GetInt64(0),
                reader.GetGuid(1),
                reader.GetString(2),
                reader.GetDateTime(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6)));
        }

        return items;
    }

    public async Task<IReadOnlyList<MontepullAnomalyDto>> GetAnomaliesAsync(long batchId, CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);
        await using var connection = OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT severity, code, message, sheet_name, source_row_no
            FROM mp_import_anomalies
            WHERE batch_id = @batchId
            ORDER BY
                CASE severity WHEN N'ERROR' THEN 0 WHEN N'WARNING' THEN 1 ELSE 2 END,
                id
            """;
        command.Parameters.AddWithValue("@batchId", batchId);

        var items = new List<MontepullAnomalyDto>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new MontepullAnomalyDto(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetInt32(4)));
        }

        return items;
    }

    public async Task<string> GetAnomaliesReportCsvAsync(long batchId, CancellationToken cancellationToken = default)
    {
        var anomalies = await GetAnomaliesAsync(batchId, cancellationToken);
        var sb = new StringBuilder();
        sb.AppendLine("Severite,Code,Message,Feuille,Ligne");
        foreach (var a in anomalies)
        {
            sb.AppendLine(string.Join(",",
                CsvField(a.Severity), CsvField(a.Code), CsvField(a.Message), CsvField(a.SheetName), CsvField(a.SourceRowNo?.ToString(CultureInfo.InvariantCulture))));
        }

        return sb.ToString();
    }

    private static string CsvField(string? value)
    {
        var v = value ?? string.Empty;
        return v.Contains(',', StringComparison.Ordinal) || v.Contains('"', StringComparison.Ordinal) || v.Contains('\n', StringComparison.Ordinal)
            ? "\"" + v.Replace("\"", "\"\"", StringComparison.Ordinal) + "\""
            : v;
    }

    public async Task<string> GetActiveDatasetAsync(CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);
        await using var connection = OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT TOP 1 active_dataset FROM mp_dataset_config ORDER BY id DESC";
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result as string ?? "DEMO";
    }

    public async Task SetActiveDatasetAsync(string datasetCode, string? updatedBy, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(datasetCode))
        {
            throw new ArgumentException("Le code de jeu de donnees est requis.", nameof(datasetCode));
        }

        await EnsureSchemaAsync(cancellationToken);
        await using var connection = OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE mp_dataset_config
            SET active_dataset = @code, updated_at = SYSUTCDATETIME(), updated_by = @by
            """;
        command.Parameters.AddWithValue("@code", datasetCode.Trim().ToUpperInvariant());
        command.Parameters.AddWithValue("@by", (object?)updatedBy ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<(string OfCode, string? OrderCode, string? ArticleCode, double Qty, string? Status)>> ListImportedManufacturingOrdersAsync(
        CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);
        await using var connection = OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT mo.code, mo.order_code, a.code, mo.quantity, mo.status
            FROM manufacturing_orders mo
            LEFT JOIN articles a ON a.id = mo.article_id
            WHERE mo.data_source = N'MONTEPULL_REAL' AND mo.is_active = 1
            ORDER BY mo.code
            """;

        var items = new List<(string, string?, string?, double, string?)>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add((
                reader.GetString(0),
                reader.IsDBNull(1) ? null : reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.GetDouble(3),
                reader.IsDBNull(4) ? null : reader.GetString(4)));
        }

        return items;
    }

    public async Task EnsureMontepullCapacityResourcesAsync(CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);
        await using var connection = OpenConnection();
        // Garantit que les ressources APS mappées Montepull existent (référentiel capacité).
        foreach (var (code, label, unit, rho) in new (string, string, string, double)[]
                 {
                     ("CC_TRICOTAGE", "Tricotage (Montepull)", "H", 0.85),
                     ("CC_TRAITEMENT", "Traitement (Montepull)", "H", 0.85),
                     ("CC_REMAILLAGE", "Remaillage (Montepull)", "H", 0.85)
                 })
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                IF OBJECT_ID('aps_capacity_resources') IS NOT NULL
                AND NOT EXISTS (SELECT 1 FROM aps_capacity_resources WHERE code = @code)
                BEGIN
                    INSERT INTO aps_capacity_resources (code, label, resource_type, unit, rho_target, is_active)
                    VALUES (@code, @label, N'MACHINE', @unit, @rho, 1);
                END
                """;
            cmd.Parameters.AddWithValue("@code", code);
            cmd.Parameters.AddWithValue("@label", label);
            cmd.Parameters.AddWithValue("@unit", unit);
            cmd.Parameters.AddWithValue("@rho", rho);
            try { await cmd.ExecuteNonQueryAsync(cancellationToken); }
            catch (SqlException)
            {
                // Schéma capacité peut différer légèrement — seed demo APS reste le filet DEMO.
            }
        }
    }

    public async Task<IReadOnlyList<ApsLaunchQuantity>> ResolveMontepullLaunchesAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(cancellationToken);
        await using var connection = OpenConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            ;WITH RemainingOf AS (
                SELECT
                    a.code AS article_code,
                    mo.code AS of_code,
                    mo.order_code,
                    CASE
                        WHEN mo.quantity - ISNULL(mo.qty_good, 0) > 0 THEN mo.quantity - ISNULL(mo.qty_good, 0)
                        ELSE mo.quantity
                    END AS remaining_qty,
                    COALESCE((
                        SELECT TOP 1 m.workcenter_code
                        FROM manufacturing_order_lines mol
                        INNER JOIN mp_import_mappings m
                            ON m.axioplan_operation_no = mol.operation_no AND m.is_active = 1
                        WHERE mol.manufacturing_order_id = mo.id AND m.workcenter_code IS NOT NULL
                        ORDER BY mol.operation_no DESC
                    ), (
                        SELECT TOP 1 m.workcenter_code
                        FROM mo_production_events e
                        INNER JOIN mp_import_mappings m
                            ON m.axioplan_operation_no = e.operation_no AND m.is_active = 1
                        WHERE e.manufacturing_order_id = mo.id AND ISNULL(e.is_active, 1) = 1
                          AND m.workcenter_code IS NOT NULL
                        ORDER BY e.operation_no DESC
                    ), N'CC_REMAILLAGE') AS resource_code
                FROM manufacturing_orders mo
                INNER JOIN articles a ON a.id = mo.article_id
                WHERE mo.data_source = N'MONTEPULL_REAL'
                  AND ISNULL(mo.is_active, 1) = 1
                  AND mo.status IN (N'PLANNED', N'RELEASED')
            ),
            RemainingSo AS (
                SELECT
                    a.code AS article_code,
                    so.code AS of_code,
                    so.code AS order_code,
                    CASE
                        WHEN ISNULL(sol.remaining_to_produce, sol.quantity) > 0
                            THEN ISNULL(sol.remaining_to_produce, sol.quantity)
                        ELSE sol.quantity
                    END AS remaining_qty,
                    N'CC_REMAILLAGE' AS resource_code
                FROM sales_order_lines sol
                INNER JOIN sales_orders so ON so.id = sol.sales_order_id
                INNER JOIN articles a ON a.id = sol.article_id
                WHERE so.data_source = N'MONTEPULL_REAL'
                  AND ISNULL(sol.remaining_to_produce, sol.quantity) > 0
                  AND NOT EXISTS (
                      SELECT 1 FROM manufacturing_orders mo
                      WHERE mo.data_source = N'MONTEPULL_REAL'
                        AND ISNULL(mo.is_active, 1) = 1
                        AND mo.article_id = sol.article_id
                        AND mo.status IN (N'PLANNED', N'RELEASED', N'CLOSED')
                  )
            )
            SELECT article_code, resource_code, SUM(remaining_qty) AS qty, MAX(order_code) AS order_code, MAX(of_code) AS of_code
            FROM (
                SELECT * FROM RemainingOf WHERE remaining_qty > 0
                UNION ALL
                SELECT * FROM RemainingSo WHERE remaining_qty > 0
            ) x
            GROUP BY article_code, resource_code
            HAVING SUM(remaining_qty) > 0
            ORDER BY article_code, resource_code
            """;

        var list = new List<ApsLaunchQuantity>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var article = reader.GetString(0);
            var resource = reader.GetString(1);
            var qty = reader.GetDouble(2);
            var orderCode = reader.IsDBNull(3) ? null : reader.GetString(3);
            var ofCode = reader.IsDBNull(4) ? null : reader.GetString(4);
            list.Add(new ApsLaunchQuantity(
                article,
                resource,
                from,
                qty,
                OrderId: orderCode ?? ofCode,
                RootArticleCode: article));
        }

        return list;
    }

    // ================================================================
    // Helpers - batch / fichiers / anomalies
    // ================================================================

    private static async Task<(long Id, Guid Uid)> InsertBatchAsync(
        SqlConnection connection, SqlTransaction transaction, string origin, string? importedBy, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO mp_import_batches (origin, imported_by, status, dataset_code)
            OUTPUT INSERTED.id, INSERTED.batch_uid
            VALUES (@origin, @importedBy, N'STAGING', N'MONTEPULL_REAL')
            """;
        command.Parameters.AddWithValue("@origin", string.IsNullOrWhiteSpace(origin) ? "UI" : origin);
        command.Parameters.AddWithValue("@importedBy", (object?)importedBy ?? DBNull.Value);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("Impossible de creer le batch d'import Montepull.");
        }

        return (reader.GetInt64(0), reader.GetGuid(1));
    }

    private static async Task<long> InsertFileAsync(
        SqlConnection connection, SqlTransaction transaction, long batchId, string fileName, string kind,
        string fileHash, IReadOnlyList<string> sheetNames, int bytesLength, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO mp_import_files (batch_id, file_name, file_kind, file_hash, sheet_names_json, bytes_length, status)
            OUTPUT INSERTED.id
            VALUES (@batchId, @fileName, @kind, @hash, @sheetNamesJson, @bytesLength, N'READ')
            """;
        command.Parameters.AddWithValue("@batchId", batchId);
        command.Parameters.AddWithValue("@fileName", fileName);
        command.Parameters.AddWithValue("@kind", kind);
        command.Parameters.AddWithValue("@hash", fileHash);
        command.Parameters.AddWithValue("@sheetNamesJson", JsonSerializer.Serialize(sheetNames));
        command.Parameters.AddWithValue("@bytesLength", bytesLength);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt64(result, CultureInfo.InvariantCulture);
    }

    private static async Task InsertAnomalyAsync(
        SqlConnection connection, SqlTransaction transaction, long batchId, long? fileId, string? sheetName, int? sourceRowNo,
        string severity, string code, string message, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO mp_import_anomalies (batch_id, file_id, sheet_name, source_row_no, severity, code, message)
            VALUES (@batchId, @fileId, @sheetName, @sourceRowNo, @severity, @code, @message)
            """;
        command.Parameters.AddWithValue("@batchId", batchId);
        command.Parameters.AddWithValue("@fileId", (object?)fileId ?? DBNull.Value);
        command.Parameters.AddWithValue("@sheetName", (object?)sheetName ?? DBNull.Value);
        command.Parameters.AddWithValue("@sourceRowNo", (object?)sourceRowNo ?? DBNull.Value);
        command.Parameters.AddWithValue("@severity", severity);
        command.Parameters.AddWithValue("@code", code);
        command.Parameters.AddWithValue("@message", message);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<HashSet<int>> LoadKnownOperationsAsync(SqlConnection connection, SqlTransaction transaction, CancellationToken cancellationToken)
    {
        var set = new HashSet<int>();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT axioplan_operation_no FROM mp_import_mappings WHERE is_active = 1";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            set.Add(reader.GetInt32(0));
        }

        return set;
    }

    private static async Task<IReadOnlyDictionary<int, int>> LoadWorkcenterMappingsAsync(SqlConnection connection, SqlTransaction transaction, CancellationToken cancellationToken)
    {
        var map = new Dictionary<int, int>();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT m.axioplan_operation_no, w.id
            FROM mp_import_mappings m
            JOIN workcenters w ON w.code = m.workcenter_code
            WHERE m.is_active = 1 AND m.workcenter_code IS NOT NULL
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            map[reader.GetInt32(0)] = reader.GetInt32(1);
        }

        return map;
    }

    private static async Task<string?> LoadBatchStatusAsync(SqlConnection connection, SqlTransaction transaction, long batchId, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT status FROM mp_import_batches WHERE id = @batchId";
        command.Parameters.AddWithValue("@batchId", batchId);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result as string;
    }

    private static async Task<long> ComputeDuplicatesDetectedAsync(
        SqlConnection connection, SqlTransaction transaction, long batchId, long attemptedRows, CancellationToken cancellationToken)
    {
        var actual = await ScalarIntAsync(connection, transaction, "SELECT COUNT(*) FROM mp_import_rows WHERE batch_id = @batchId",
            [new SqlParameter("@batchId", batchId)], cancellationToken) ?? 0;
        return Math.Max(0, attemptedRows - actual);
    }

    private static async Task<MontepullImportSummaryDto> BuildSummaryAsync(
        SqlConnection connection, SqlTransaction transaction, long batchId, string status, long duplicatesDetected, CancellationToken cancellationToken)
    {
        var batchUid = await ScalarGuidAsync(connection, transaction, "SELECT batch_uid FROM mp_import_batches WHERE id = @batchId",
            [new SqlParameter("@batchId", batchId)], cancellationToken) ?? Guid.Empty;

        var linesRead = await ScalarIntAsync(connection, transaction, """
            SELECT
              (SELECT COUNT(*) FROM mp_staging_commandes WHERE batch_id = @batchId)
              + (SELECT COUNT(*) FROM mp_staging_suivi_ops WHERE batch_id = @batchId)
              + (SELECT COUNT(*) FROM mp_staging_nomenclature WHERE batch_id = @batchId)
              + (SELECT COUNT(*) FROM mp_staging_gamme WHERE batch_id = @batchId)
            """,
            [new SqlParameter("@batchId", batchId)], cancellationToken) ?? 0;
        var linesValid = await ScalarIntAsync(connection, transaction, """
            SELECT
              (SELECT COUNT(*) FROM mp_staging_commandes WHERE batch_id = @batchId AND status = N'STAGED')
              + (SELECT COUNT(*) FROM mp_staging_suivi_ops WHERE batch_id = @batchId AND status = N'STAGED')
            """,
            [new SqlParameter("@batchId", batchId)], cancellationToken) ?? 0;
        var linesWarning = await ScalarIntAsync(connection, transaction, """
            SELECT
              (SELECT COUNT(*) FROM mp_staging_commandes WHERE batch_id = @batchId AND status = N'WARNING')
              + (SELECT COUNT(*) FROM mp_staging_suivi_ops WHERE batch_id = @batchId AND status = N'WARNING')
            """,
            [new SqlParameter("@batchId", batchId)], cancellationToken) ?? 0;
        var linesRejected = await ScalarIntAsync(connection, transaction, """
            SELECT
              (SELECT COUNT(*) FROM mp_staging_commandes WHERE batch_id = @batchId AND status = N'REJECTED')
              + (SELECT COUNT(*) FROM mp_staging_suivi_ops WHERE batch_id = @batchId AND status = N'REJECTED')
            """,
            [new SqlParameter("@batchId", batchId)], cancellationToken) ?? 0;
        var commandesDetected = await ScalarIntAsync(connection, transaction, "SELECT COUNT(*) FROM mp_staging_commandes WHERE batch_id = @batchId",
            [new SqlParameter("@batchId", batchId)], cancellationToken) ?? 0;
        var ofDetected = await ScalarIntAsync(connection, transaction, "SELECT COUNT(DISTINCT of_code) FROM mp_staging_suivi_ops WHERE batch_id = @batchId",
            [new SqlParameter("@batchId", batchId)], cancellationToken) ?? 0;
        var operationsDetected = await ScalarIntAsync(connection, transaction, "SELECT COUNT(DISTINCT operation_no) FROM mp_staging_suivi_ops WHERE batch_id = @batchId AND operation_no IS NOT NULL",
            [new SqlParameter("@batchId", batchId)], cancellationToken) ?? 0;
        var articlesDetected = await ScalarIntAsync(connection, transaction, "SELECT COUNT(*) FROM mp_staging_articles WHERE batch_id = @batchId",
            [new SqlParameter("@batchId", batchId)], cancellationToken) ?? 0;

        var anomalies = new List<MontepullAnomalyDto>();
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                SELECT severity, code, message, sheet_name, source_row_no
                FROM mp_import_anomalies
                WHERE batch_id = @batchId
                ORDER BY CASE severity WHEN N'ERROR' THEN 0 WHEN N'WARNING' THEN 1 ELSE 2 END, id
                """;
            command.Parameters.AddWithValue("@batchId", batchId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                anomalies.Add(new MontepullAnomalyDto(
                    reader.GetString(0), reader.GetString(1), reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetInt32(4)));
            }
        }

        return new MontepullImportSummaryDto(
            batchId, batchUid, status, linesRead, linesValid, linesWarning, linesRejected,
            commandesDetected, ofDetected, operationsDetected, articlesDetected, (int)duplicatesDetected, anomalies);
    }

    private static async Task UpdateBatchSummaryAsync(
        SqlConnection connection, SqlTransaction transaction, long batchId, MontepullImportSummaryDto summary, string message, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "UPDATE mp_import_batches SET summary_json = @summaryJson, message = @message WHERE id = @batchId";
        command.Parameters.AddWithValue("@summaryJson", JsonSerializer.Serialize(summary));
        command.Parameters.AddWithValue("@message", message);
        command.Parameters.AddWithValue("@batchId", batchId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    // ================================================================
    // Helpers - referentiel metier
    // ================================================================

    private static async Task<int> EnsureFinishedGoodFamilyAsync(SqlConnection connection, SqlTransaction transaction, CancellationToken cancellationToken)
    {
        var existing = await ScalarIntAsync(connection, transaction, "SELECT id FROM article_families WHERE code = N'PULL'", [], cancellationToken);
        if (existing is not null)
        {
            return existing.Value;
        }

        var categoryId = await ScalarIntAsync(connection, transaction, "SELECT id FROM article_categories WHERE code = N'FINISHED_GOOD'", [], cancellationToken)
            ?? throw new InvalidOperationException("Categorie article FINISHED_GOOD introuvable.");

        return await InsertAndReturnIdAsync(connection, transaction,
            """
            INSERT INTO article_families (category_id, code, label)
            OUTPUT INSERTED.id
            VALUES (@categoryId, N'PULL', N'Pull')
            """,
            [new SqlParameter("@categoryId", categoryId)], cancellationToken);
    }

    private static async Task<int> EnsureProductFamilyAsync(SqlConnection connection, SqlTransaction transaction, int fgFamilyId, CancellationToken cancellationToken)
    {
        var existing = await ScalarIntAsync(connection, transaction,
            "SELECT TOP 1 id FROM product_families WHERE article_family_id = @familyId ORDER BY id",
            [new SqlParameter("@familyId", fgFamilyId)], cancellationToken);
        if (existing is not null)
        {
            return existing.Value;
        }

        return await InsertAndReturnIdAsync(connection, transaction,
            """
            INSERT INTO product_families (article_family_id, code, label)
            OUTPUT INSERTED.id
            VALUES (@familyId, N'MONTEPULL_IMPORT', N'Import Montepull (reel)')
            """,
            [new SqlParameter("@familyId", fgFamilyId)], cancellationToken);
    }

    private static async Task<int> EnsureComponentFamilyAsync(SqlConnection connection, SqlTransaction transaction, CancellationToken cancellationToken)
    {
        var existing = await ScalarIntAsync(connection, transaction, "SELECT id FROM article_families WHERE code = N'IMPORTED_COMPONENTS'", [], cancellationToken);
        if (existing is not null)
        {
            return existing.Value;
        }

        var categoryId = await ScalarIntAsync(connection, transaction, "SELECT id FROM article_categories WHERE code = N'COMPONENT'", [], cancellationToken);
        if (categoryId is null)
        {
            categoryId = await InsertAndReturnIdAsync(connection, transaction,
                """
                INSERT INTO article_categories (code, label)
                OUTPUT INSERTED.id
                VALUES (N'COMPONENT', N'Composant')
                """,
                [], cancellationToken);
        }

        return await InsertAndReturnIdAsync(connection, transaction,
            """
            INSERT INTO article_families (category_id, code, label)
            OUTPUT INSERTED.id
            VALUES (@categoryId, N'IMPORTED_COMPONENTS', N'Composants importes Montepull')
            """,
            [new SqlParameter("@categoryId", categoryId.Value)], cancellationToken);
    }

    private static async Task<int> EnsureCustomerAsync(
        SqlConnection connection, SqlTransaction transaction, string customerCode, string? customerLabel, CancellationToken cancellationToken)
    {
        var existing = await ScalarIntAsync(connection, transaction, "SELECT id FROM customers WHERE code = @code",
            [new SqlParameter("@code", customerCode)], cancellationToken);
        if (existing is not null)
        {
            return existing.Value;
        }

        return await InsertAndReturnIdAsync(connection, transaction,
            """
            INSERT INTO customers (code, label, status)
            OUTPUT INSERTED.id
            VALUES (@code, @label, N'ACTIVE')
            """,
            [
                new SqlParameter("@code", customerCode),
                new SqlParameter("@label", string.IsNullOrWhiteSpace(customerLabel) ? customerCode : customerLabel!.Trim())
            ],
            cancellationToken);
    }

    private static async Task<int> EnsureArticleAsync(
        SqlConnection connection, SqlTransaction transaction, int familyId, string code, string label, string articleType,
        string unit, int? customerId, CancellationToken cancellationToken)
    {
        var existing = await ScalarIntAsync(connection, transaction, "SELECT id FROM articles WHERE code = @code",
            [new SqlParameter("@code", code)], cancellationToken);
        if (existing is not null)
        {
            await using var update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = """
                UPDATE articles
                SET label = @label, customer_id = COALESCE(@customerId, customer_id)
                WHERE id = @id
                """;
            update.Parameters.AddWithValue("@label", label);
            update.Parameters.AddWithValue("@customerId", (object?)customerId ?? DBNull.Value);
            update.Parameters.AddWithValue("@id", existing.Value);
            await update.ExecuteNonQueryAsync(cancellationToken);
            return existing.Value;
        }

        return await InsertAndReturnIdAsync(connection, transaction,
            """
            INSERT INTO articles (family_id, code, label, article_type, default_unit, status, source_system, creation_mode, customer_id)
            OUTPUT INSERTED.id
            VALUES (@familyId, @code, @label, @articleType, @unit, N'ACTIVE', N'MONTEPULL_IMPORT', N'IMPORTED', @customerId)
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

    private static async Task<int> UpsertSalesOrderAsync(
        SqlConnection connection, SqlTransaction transaction, string orderCode, string? customerCode, long batchId, CancellationToken cancellationToken)
    {
        var existing = await ScalarIntAsync(connection, transaction, "SELECT id FROM sales_orders WHERE code = @code",
            [new SqlParameter("@code", orderCode)], cancellationToken);
        if (existing is int id)
        {
            await using var update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = """
                UPDATE sales_orders
                SET customer_code = COALESCE(@customerCode, customer_code), data_source = N'MONTEPULL_REAL', import_batch_id = @batchId
                WHERE id = @id
                """;
            update.Parameters.AddWithValue("@customerCode", (object?)customerCode ?? DBNull.Value);
            update.Parameters.AddWithValue("@batchId", batchId);
            update.Parameters.AddWithValue("@id", id);
            await update.ExecuteNonQueryAsync(cancellationToken);
            return id;
        }

        return await InsertAndReturnIdAsync(connection, transaction,
            """
            INSERT INTO sales_orders (code, customer_code, status, data_source, import_batch_id)
            OUTPUT INSERTED.id
            VALUES (@code, @customerCode, N'OPEN', N'MONTEPULL_REAL', @batchId)
            """,
            [
                new SqlParameter("@code", orderCode),
                new SqlParameter("@customerCode", (object?)customerCode ?? DBNull.Value),
                new SqlParameter("@batchId", batchId)
            ],
            cancellationToken);
    }

    private static async Task UpsertSalesOrderLineAsync(
        SqlConnection connection, SqlTransaction transaction, int salesOrderId, int articleId, double qtyOrdered, double qtyLaunched,
        double qtyProduced, int? lastOperation, double remainingToLaunch, double remainingToProduce, string externalRef, long batchId,
        CancellationToken cancellationToken)
    {
        var existing = await ScalarIntAsync(connection, transaction,
            "SELECT id FROM sales_order_lines WHERE sales_order_id = @salesOrderId AND article_id = @articleId",
            [new SqlParameter("@salesOrderId", salesOrderId), new SqlParameter("@articleId", articleId)], cancellationToken);

        if (existing is int lineId)
        {
            await using var update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = """
                UPDATE sales_order_lines
                SET quantity = @qtyOrdered, qty_launched = @qtyLaunched, qty_produced = @qtyProduced,
                    last_operation = @lastOperation, remaining_to_launch = @remainingToLaunch, remaining_to_produce = @remainingToProduce,
                    data_source = N'MONTEPULL_REAL', import_batch_id = @batchId
                WHERE id = @id
                """;
            update.Parameters.AddWithValue("@qtyOrdered", qtyOrdered);
            update.Parameters.AddWithValue("@qtyLaunched", qtyLaunched);
            update.Parameters.AddWithValue("@qtyProduced", qtyProduced);
            update.Parameters.AddWithValue("@lastOperation", (object?)lastOperation ?? DBNull.Value);
            update.Parameters.AddWithValue("@remainingToLaunch", remainingToLaunch);
            update.Parameters.AddWithValue("@remainingToProduce", remainingToProduce);
            update.Parameters.AddWithValue("@batchId", batchId);
            update.Parameters.AddWithValue("@id", lineId);
            await update.ExecuteNonQueryAsync(cancellationToken);
            return;
        }

        var nextLineNo = (await ScalarIntAsync(connection, transaction,
            "SELECT COALESCE(MAX(line_no), 0) + 1 FROM sales_order_lines WHERE sales_order_id = @salesOrderId",
            [new SqlParameter("@salesOrderId", salesOrderId)], cancellationToken)) ?? 1;

        await using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT INTO sales_order_lines (
                sales_order_id, line_no, article_id, quantity, unit,
                qty_launched, qty_produced, last_operation, remaining_to_launch, remaining_to_produce,
                data_source, import_batch_id, external_ref
            )
            VALUES (
                @salesOrderId, @lineNo, @articleId, @qtyOrdered, N'PIECE',
                @qtyLaunched, @qtyProduced, @lastOperation, @remainingToLaunch, @remainingToProduce,
                N'MONTEPULL_REAL', @batchId, @externalRef
            )
            """;
        insert.Parameters.AddWithValue("@salesOrderId", salesOrderId);
        insert.Parameters.AddWithValue("@lineNo", nextLineNo);
        insert.Parameters.AddWithValue("@articleId", articleId);
        insert.Parameters.AddWithValue("@qtyOrdered", qtyOrdered);
        insert.Parameters.AddWithValue("@qtyLaunched", qtyLaunched);
        insert.Parameters.AddWithValue("@qtyProduced", qtyProduced);
        insert.Parameters.AddWithValue("@lastOperation", (object?)lastOperation ?? DBNull.Value);
        insert.Parameters.AddWithValue("@remainingToLaunch", remainingToLaunch);
        insert.Parameters.AddWithValue("@remainingToProduce", remainingToProduce);
        insert.Parameters.AddWithValue("@batchId", batchId);
        insert.Parameters.AddWithValue("@externalRef", externalRef);
        await insert.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<int> UpsertManufacturingOrderAsync(
        SqlConnection connection, SqlTransaction transaction, string ofCode, int articleId, double qty, string status,
        string? orderCode, DateTime? actualStart, DateTime? actualEnd, double qtyGood, double qtyRejected, int? lastOperation,
        DateTime? deliveryDate, long batchId, CancellationToken cancellationToken)
    {
        var existing = await ScalarIntAsync(connection, transaction, "SELECT id FROM manufacturing_orders WHERE code = @code",
            [new SqlParameter("@code", ofCode)], cancellationToken);

        if (existing is int id)
        {
            await using var update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = """
                UPDATE manufacturing_orders
                SET article_id = @articleId, quantity = @qty, status = @status, order_code = @orderCode,
                    actual_start = @actualStart, actual_end = @actualEnd, qty_good = @qtyGood, qty_rejected = @qtyRejected,
                    last_operation = @lastOperation, delivery_date = @deliveryDate,
                    data_source = N'MONTEPULL_REAL', import_batch_id = @batchId, is_active = 1
                WHERE id = @id
                """;
            AddManufacturingOrderParameters(update, articleId, qty, status, orderCode, actualStart, actualEnd, qtyGood, qtyRejected, lastOperation, deliveryDate, batchId);
            update.Parameters.AddWithValue("@id", id);
            await update.ExecuteNonQueryAsync(cancellationToken);
            return id;
        }

        await using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT INTO manufacturing_orders (
                code, article_id, quantity, unit, status, order_code,
                actual_start, actual_end, qty_good, qty_rejected, last_operation, delivery_date,
                data_source, import_batch_id, is_active
            )
            OUTPUT INSERTED.id
            VALUES (
                @code, @articleId, @qty, N'PIECE', @status, @orderCode,
                @actualStart, @actualEnd, @qtyGood, @qtyRejected, @lastOperation, @deliveryDate,
                N'MONTEPULL_REAL', @batchId, 1
            )
            """;
        insert.Parameters.AddWithValue("@code", ofCode);
        AddManufacturingOrderParameters(insert, articleId, qty, status, orderCode, actualStart, actualEnd, qtyGood, qtyRejected, lastOperation, deliveryDate, batchId);
        var result = await insert.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(result, CultureInfo.InvariantCulture);
    }

    private static void AddManufacturingOrderParameters(
        SqlCommand command, int articleId, double qty, string status, string? orderCode, DateTime? actualStart, DateTime? actualEnd,
        double qtyGood, double qtyRejected, int? lastOperation, DateTime? deliveryDate, long batchId)
    {
        command.Parameters.AddWithValue("@articleId", articleId);
        command.Parameters.AddWithValue("@qty", qty);
        command.Parameters.AddWithValue("@status", status);
        command.Parameters.AddWithValue("@orderCode", (object?)orderCode ?? DBNull.Value);
        command.Parameters.AddWithValue("@actualStart", (object?)actualStart ?? DBNull.Value);
        command.Parameters.AddWithValue("@actualEnd", (object?)actualEnd ?? DBNull.Value);
        command.Parameters.AddWithValue("@qtyGood", qtyGood);
        command.Parameters.AddWithValue("@qtyRejected", qtyRejected);
        command.Parameters.AddWithValue("@lastOperation", (object?)lastOperation ?? DBNull.Value);
        command.Parameters.AddWithValue("@deliveryDate", (object?)deliveryDate ?? DBNull.Value);
        command.Parameters.AddWithValue("@batchId", batchId);
    }

    private static string MapManufacturingOrderStatus(string? statusText)
    {
        if (string.IsNullOrWhiteSpace(statusText))
        {
            return "PLANNED";
        }

        var normalized = MontepullFileDetector.Normalize(statusText);
        if (normalized.Contains("termine", StringComparison.Ordinal))
        {
            return "CLOSED";
        }

        if (normalized.Contains("en cours", StringComparison.Ordinal))
        {
            return "RELEASED";
        }

        return "PLANNED";
    }

    private static async Task<int> EnsureBomBaseAsync(SqlConnection connection, SqlTransaction transaction, int productFamilyId, string bomCode, CancellationToken cancellationToken)
    {
        var existing = await ScalarIntAsync(connection, transaction, "SELECT id FROM bom_bases WHERE code = @code",
            [new SqlParameter("@code", bomCode)], cancellationToken);
        if (existing is not null)
        {
            return existing.Value;
        }

        return await InsertAndReturnIdAsync(connection, transaction,
            """
            INSERT INTO bom_bases (product_family_id, code, version, status)
            OUTPUT INSERTED.id
            VALUES (@productFamilyId, @code, 1, N'VALIDATED')
            """,
            [new SqlParameter("@productFamilyId", productFamilyId), new SqlParameter("@code", bomCode)], cancellationToken);
    }

    private static async Task UpsertArticleBomAssignmentAsync(
        SqlConnection connection, SqlTransaction transaction, int articleId, int bomId, CancellationToken cancellationToken)
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

    private static async Task UpsertBomLineAsync(
        SqlConnection connection, SqlTransaction transaction, int bomId, int componentArticleId, double quantity, string unit, CancellationToken cancellationToken)
    {
        var existing = await ScalarIntAsync(connection, transaction,
            "SELECT id FROM bom_base_lines WHERE bom_base_id = @bomId AND component_article_id = @componentArticleId",
            [new SqlParameter("@bomId", bomId), new SqlParameter("@componentArticleId", componentArticleId)], cancellationToken);

        if (existing is int lineId)
        {
            await using var update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = "UPDATE bom_base_lines SET quantity_base = @quantity, unit = @unit WHERE id = @id";
            update.Parameters.AddWithValue("@quantity", quantity);
            update.Parameters.AddWithValue("@unit", unit);
            update.Parameters.AddWithValue("@id", lineId);
            await update.ExecuteNonQueryAsync(cancellationToken);
            return;
        }

        var nextLineNo = (await ScalarIntAsync(connection, transaction,
            "SELECT COALESCE(MAX(line_no), 0) + 1 FROM bom_base_lines WHERE bom_base_id = @bomId",
            [new SqlParameter("@bomId", bomId)], cancellationToken)) ?? 1;

        await using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT INTO bom_base_lines (bom_base_id, line_no, component_article_id, quantity_base, unit, loss_rate, behavior)
            VALUES (@bomId, @lineNo, @componentArticleId, @quantity, @unit, 0, N'FIXED')
            """;
        insert.Parameters.AddWithValue("@bomId", bomId);
        insert.Parameters.AddWithValue("@lineNo", nextLineNo);
        insert.Parameters.AddWithValue("@componentArticleId", componentArticleId);
        insert.Parameters.AddWithValue("@quantity", quantity);
        insert.Parameters.AddWithValue("@unit", unit);
        await insert.ExecuteNonQueryAsync(cancellationToken);
    }

    // ================================================================
    // Helpers - SQL generiques
    // ================================================================

    private static async Task<int?> ScalarIntAsync(
        SqlConnection connection, SqlTransaction transaction, string sql, IReadOnlyList<SqlParameter> parameters, CancellationToken cancellationToken)
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
            double doubleValue => (int)doubleValue,
            _ => null
        };
    }

    private static async Task<bool> ScalarBoolAsync(
        SqlConnection connection, SqlTransaction transaction, string sql, IReadOnlyList<SqlParameter> parameters, CancellationToken cancellationToken)
    {
        var value = await ScalarIntAsync(connection, transaction, sql, parameters, cancellationToken);
        return value == 1;
    }

    private static async Task<Guid?> ScalarGuidAsync(
        SqlConnection connection, SqlTransaction transaction, string sql, IReadOnlyList<SqlParameter> parameters, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var parameter in parameters)
        {
            command.Parameters.Add(parameter);
        }

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is Guid guid ? guid : null;
    }

    private static async Task<int> InsertAndReturnIdAsync(
        SqlConnection connection, SqlTransaction transaction, string sql, IReadOnlyList<SqlParameter> parameters, CancellationToken cancellationToken)
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

    /// <summary>
    /// Insertion rapide via SqlBulkCopy (gros volumes SuiviOperations). Fallback VALUES si conflit unique.
    /// </summary>
    private static async Task BulkCopyAsync(
        SqlConnection connection, SqlTransaction transaction, string table, string[] columns, List<object?[]> rows, CancellationToken cancellationToken)
    {
        if (rows.Count == 0)
        {
            return;
        }

        var tableData = new DataTable();
        foreach (var column in columns)
        {
            tableData.Columns.Add(column, typeof(object));
        }

        foreach (var row in rows)
        {
            var dataRow = tableData.NewRow();
            for (var i = 0; i < columns.Length; i++)
            {
                dataRow[i] = row[i] ?? DBNull.Value;
            }

            tableData.Rows.Add(dataRow);
        }

        try
        {
            using var bulk = new SqlBulkCopy(connection, SqlBulkCopyOptions.Default, transaction)
            {
                DestinationTableName = table,
                BatchSize = Math.Min(5000, rows.Count),
                BulkCopyTimeout = 600
            };
            for (var i = 0; i < columns.Length; i++)
            {
                bulk.ColumnMappings.Add(i, columns[i]);
            }

            await bulk.WriteToServerAsync(tableData, cancellationToken);
        }
        catch (SqlException ex) when (ex.Number is 2627 or 2601)
        {
            await InsertBatchedAsync(connection, transaction, table, columns, rows, cancellationToken);
        }
    }

    /// <summary>
    /// Insertion en lots (VALUES multiples). En cas de doublon, retente ligne a ligne (idempotence).
    /// </summary>
    private static async Task InsertBatchedAsync(
        SqlConnection connection, SqlTransaction transaction, string table, string[] columns, List<object?[]> rows, CancellationToken cancellationToken)
    {
        if (rows.Count == 0)
        {
            return;
        }

        var effectiveBatchSize = Math.Max(1, Math.Min(200, 2000 / Math.Max(1, columns.Length)));
        for (var offset = 0; offset < rows.Count; offset += effectiveBatchSize)
        {
            var chunk = rows.Skip(offset).Take(effectiveBatchSize).ToList();
            try
            {
                await ExecuteInsertChunkAsync(connection, transaction, table, columns, chunk, cancellationToken);
            }
            catch (SqlException ex) when (ex.Number is 2627 or 2601)
            {
                foreach (var row in chunk)
                {
                    try
                    {
                        await ExecuteInsertChunkAsync(connection, transaction, table, columns, [row], cancellationToken);
                    }
                    catch (SqlException innerEx) when (innerEx.Number is 2627 or 2601)
                    {
                        // Ligne en conflit (row_hash / cle metier deja presente pour ce batch) : ignoree, idempotence.
                    }
                }
            }
        }
    }

    private static async Task ExecuteInsertChunkAsync(
        SqlConnection connection, SqlTransaction transaction, string table, string[] columns, List<object?[]> rows, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        var sb = new StringBuilder();
        sb.Append("INSERT INTO ").Append(table).Append(" (").Append(string.Join(",", columns)).Append(") VALUES ");
        var groups = new List<string>(rows.Count);
        var paramIndex = 0;

        foreach (var row in rows)
        {
            var placeholders = new string[row.Length];
            for (var c = 0; c < row.Length; c++)
            {
                var pname = "@p" + paramIndex++;
                placeholders[c] = pname;
                command.Parameters.AddWithValue(pname, row[c] ?? DBNull.Value);
            }

            groups.Add("(" + string.Join(",", placeholders) + ")");
        }

        sb.Append(string.Join(",", groups));
        command.CommandText = sb.ToString();
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private SqlConnection OpenConnection()
    {
        var connectionString = options.Value.ConnectionString
            ?? throw new InvalidOperationException("Connection string manquante : Database:ConnectionString");
        var connection = new SqlConnection(connectionString);
        connection.Open();
        return connection;
    }

    // ================================================================
    // Helpers - Excel
    // ================================================================

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
            // Format complet (avec heure) : necessaire pour le calcul des durees SuiviOperations/ListeSuivi.
            DateTime date => date.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            double number when Math.Abs(number % 1) < double.Epsilon => number.ToString("0", CultureInfo.InvariantCulture),
            double number => number.ToString("0.######", CultureInfo.InvariantCulture),
            float number => number.ToString("0.######", CultureInfo.InvariantCulture),
            _ => value.ToString()?.Trim() ?? string.Empty
        };
    }

    private static string CellAt(WorkbookSheet sheet, int excelRowNo, int excelColNo)
        => excelRowNo <= 0 || excelRowNo > sheet.Rows.Count ? string.Empty : CellAt(sheet.Rows[excelRowNo - 1], excelColNo - 1);

    private static string CellAt(IReadOnlyList<string> row, int index)
        => index >= 0 && index < row.Count ? row[index] : string.Empty;

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

    private static string SerializeRow(IReadOnlyList<string> row) => JsonSerializer.Serialize(row);

    private sealed record WorkbookSheet(string Name, List<IReadOnlyList<string>> Rows, int ColumnCount);

    private sealed record OfSummary(
        string OfCode, string? ArticleCode, string? Designation, string? OrderCode, double? OfQuantity,
        string? StatusText, DateTime? FirstStart, DateTime? LastEnd, DateTime? DeliveryDate);

    // ================================================================
    // Schema DDL (equivalent database/montepull_import_schema.sql)
    // ================================================================

    private const string SchemaDdl = """
        IF OBJECT_ID(N'dbo.mp_import_batches', N'U') IS NULL
        BEGIN
            CREATE TABLE mp_import_batches (
                id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                batch_uid UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
                origin NVARCHAR(100) NOT NULL DEFAULT N'UI',
                imported_by NVARCHAR(100) NULL,
                imported_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
                status NVARCHAR(30) NOT NULL DEFAULT N'STAGING',
                dataset_code NVARCHAR(40) NOT NULL DEFAULT N'MONTEPULL_REAL',
                summary_json NVARCHAR(MAX) NULL,
                message NVARCHAR(1000) NULL
            );
        END;

        IF OBJECT_ID(N'dbo.mp_import_files', N'U') IS NULL
        BEGIN
            CREATE TABLE mp_import_files (
                id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                batch_id BIGINT NOT NULL,
                file_name NVARCHAR(255) NOT NULL,
                file_kind NVARCHAR(40) NOT NULL,
                file_hash NVARCHAR(64) NOT NULL,
                sheet_names_json NVARCHAR(MAX) NULL,
                bytes_length INT NOT NULL DEFAULT 0,
                status NVARCHAR(30) NOT NULL DEFAULT N'READ',
                FOREIGN KEY (batch_id) REFERENCES mp_import_batches(id),
                CONSTRAINT uq_mp_import_files_batch_hash UNIQUE (batch_id, file_hash)
            );
        END;

        IF OBJECT_ID(N'dbo.mp_import_rows', N'U') IS NULL
        BEGIN
            CREATE TABLE mp_import_rows (
                id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                batch_id BIGINT NOT NULL,
                file_id BIGINT NOT NULL,
                sheet_name NVARCHAR(120) NOT NULL,
                source_row_no INT NOT NULL,
                row_hash NVARCHAR(64) NOT NULL,
                status NVARCHAR(30) NOT NULL DEFAULT N'STAGED',
                error_message NVARCHAR(1000) NULL,
                raw_json NVARCHAR(MAX) NULL,
                FOREIGN KEY (batch_id) REFERENCES mp_import_batches(id),
                FOREIGN KEY (file_id) REFERENCES mp_import_files(id),
                CONSTRAINT uq_mp_import_rows_hash UNIQUE (batch_id, row_hash)
            );
        END;

        IF OBJECT_ID(N'dbo.mp_staging_commandes', N'U') IS NULL
        BEGIN
            CREATE TABLE mp_staging_commandes (
                id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                batch_id BIGINT NOT NULL,
                file_id BIGINT NOT NULL,
                source_row_no INT NOT NULL,
                row_hash NVARCHAR(64) NOT NULL,
                order_code NVARCHAR(100) NOT NULL,
                customer_code NVARCHAR(50) NULL,
                article_code NVARCHAR(100) NOT NULL,
                designation NVARCHAR(255) NULL,
                qty_ordered FLOAT NOT NULL DEFAULT 0,
                qty_launched FLOAT NOT NULL DEFAULT 0,
                somme_operations FLOAT NULL,
                somme_op_70_commande FLOAT NULL,
                somme_op_70_article FLOAT NULL,
                last_operation INT NULL,
                qty_produced FLOAT NOT NULL DEFAULT 0,
                remaining_to_launch FLOAT NOT NULL DEFAULT 0,
                remaining_to_produce FLOAT NOT NULL DEFAULT 0,
                launch_status NVARCHAR(40) NULL,
                status NVARCHAR(30) NOT NULL DEFAULT N'STAGED',
                FOREIGN KEY (batch_id) REFERENCES mp_import_batches(id),
                CONSTRAINT uq_mp_staging_commandes_hash UNIQUE (batch_id, row_hash)
            );
        END;

        IF OBJECT_ID(N'dbo.mp_staging_suivi_ops', N'U') IS NULL
        BEGIN
            CREATE TABLE mp_staging_suivi_ops (
                id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                batch_id BIGINT NOT NULL,
                file_id BIGINT NOT NULL,
                sheet_name NVARCHAR(120) NOT NULL,
                source_row_no INT NOT NULL,
                row_hash NVARCHAR(64) NOT NULL,
                source_kind NVARCHAR(20) NOT NULL DEFAULT N'LISTE',
                of_code NVARCHAR(100) NOT NULL,
                order_code NVARCHAR(100) NULL,
                article_code NVARCHAR(100) NULL,
                designation NVARCHAR(255) NULL,
                operation_no INT NULL,
                actual_start DATETIME2 NULL,
                actual_end DATETIME2 NULL,
                qty_good FLOAT NOT NULL DEFAULT 0,
                qty_rejected FLOAT NOT NULL DEFAULT 0,
                of_quantity FLOAT NULL,
                qty_ordered FLOAT NULL,
                weight_value FLOAT NULL,
                status_text NVARCHAR(80) NULL,
                atelier NVARCHAR(100) NULL,
                packet_no NVARCHAR(50) NULL,
                customer_code NVARCHAR(50) NULL,
                customer_name NVARCHAR(255) NULL,
                order_date DATE NULL,
                delivery_date DATE NULL,
                customer_order_ref NVARCHAR(100) NULL,
                duration_seconds FLOAT NULL,
                duration_kind NVARCHAR(30) NULL,
                status NVARCHAR(30) NOT NULL DEFAULT N'STAGED',
                FOREIGN KEY (batch_id) REFERENCES mp_import_batches(id),
                CONSTRAINT uq_mp_staging_suivi_hash UNIQUE (batch_id, row_hash)
            );
        END;

        IF OBJECT_ID(N'dbo.mp_staging_articles', N'U') IS NULL
        BEGIN
            CREATE TABLE mp_staging_articles (
                id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                batch_id BIGINT NOT NULL,
                article_code NVARCHAR(100) NOT NULL,
                designation NVARCHAR(255) NULL,
                source_file NVARCHAR(255) NULL,
                status NVARCHAR(30) NOT NULL DEFAULT N'STAGED',
                CONSTRAINT uq_mp_staging_articles UNIQUE (batch_id, article_code)
            );
        END;

        IF OBJECT_ID(N'dbo.mp_staging_nomenclature', N'U') IS NULL
        BEGIN
            CREATE TABLE mp_staging_nomenclature (
                id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                batch_id BIGINT NOT NULL,
                file_id BIGINT NOT NULL,
                source_row_no INT NOT NULL,
                row_hash NVARCHAR(64) NOT NULL,
                parent_article_code NVARCHAR(100) NOT NULL,
                component_code NVARCHAR(100) NULL,
                component_label NVARCHAR(255) NULL,
                quantity_base FLOAT NULL,
                unit NVARCHAR(20) NULL,
                supplier NVARCHAR(100) NULL,
                loss_rate FLOAT NULL,
                is_calculated BIT NOT NULL DEFAULT 0,
                raw_json NVARCHAR(MAX) NULL,
                status NVARCHAR(30) NOT NULL DEFAULT N'STAGED',
                CONSTRAINT uq_mp_staging_nomen_hash UNIQUE (batch_id, row_hash)
            );
        END;

        IF OBJECT_ID(N'dbo.mp_staging_gamme', N'U') IS NULL
        BEGIN
            CREATE TABLE mp_staging_gamme (
                id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                batch_id BIGINT NOT NULL,
                of_code NVARCHAR(100) NOT NULL,
                article_code NVARCHAR(100) NULL,
                operation_no INT NOT NULL,
                qty_at_op FLOAT NULL,
                status NVARCHAR(30) NOT NULL DEFAULT N'STAGED',
                CONSTRAINT uq_mp_staging_gamme UNIQUE (batch_id, of_code, operation_no)
            );
        END;

        IF OBJECT_ID(N'dbo.mp_import_anomalies', N'U') IS NULL
        BEGIN
            CREATE TABLE mp_import_anomalies (
                id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                batch_id BIGINT NOT NULL,
                file_id BIGINT NULL,
                sheet_name NVARCHAR(120) NULL,
                source_row_no INT NULL,
                severity NVARCHAR(20) NOT NULL,
                code NVARCHAR(60) NOT NULL,
                message NVARCHAR(1000) NOT NULL,
                context_json NVARCHAR(MAX) NULL,
                created_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
                FOREIGN KEY (batch_id) REFERENCES mp_import_batches(id)
            );
        END;

        IF OBJECT_ID(N'dbo.mp_import_mappings', N'U') IS NULL
        BEGIN
            CREATE TABLE mp_import_mappings (
                id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                source_operation_code NVARCHAR(40) NOT NULL,
                axioplan_operation_no INT NOT NULL,
                workcenter_code NVARCHAR(50) NULL,
                sequence_no INT NULL,
                unit NVARCHAR(20) NULL,
                event_nature NVARCHAR(40) NULL,
                is_active BIT NOT NULL DEFAULT 1,
                notes NVARCHAR(500) NULL,
                CONSTRAINT uq_mp_import_mappings_src UNIQUE (source_operation_code)
            );
        END;

        IF OBJECT_ID(N'dbo.mp_of_operation_agg', N'U') IS NULL
        BEGIN
            CREATE TABLE mp_of_operation_agg (
                id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                batch_id BIGINT NOT NULL,
                of_code NVARCHAR(100) NOT NULL,
                operation_no INT NOT NULL,
                qty_good_total FLOAT NOT NULL DEFAULT 0,
                qty_rejected_total FLOAT NOT NULL DEFAULT 0,
                first_start DATETIME2 NULL,
                last_end DATETIME2 NULL,
                packet_count INT NOT NULL DEFAULT 0,
                status_agg NVARCHAR(40) NULL,
                remaining_qty FLOAT NULL,
                CONSTRAINT uq_mp_of_op_agg UNIQUE (batch_id, of_code, operation_no)
            );
        END;

        IF OBJECT_ID(N'dbo.mp_duration_stats', N'U') IS NULL
        BEGIN
            CREATE TABLE mp_duration_stats (
                id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                batch_id BIGINT NOT NULL,
                operation_no INT NOT NULL,
                observation_count INT NOT NULL,
                mean_seconds FLOAT NULL,
                median_seconds FLOAT NULL,
                min_seconds FLOAT NULL,
                max_seconds FLOAT NULL,
                p25_seconds FLOAT NULL,
                p75_seconds FLOAT NULL,
                stddev_seconds FLOAT NULL,
                excluded_anomaly_count INT NOT NULL DEFAULT 0,
                CONSTRAINT uq_mp_duration_stats UNIQUE (batch_id, operation_no)
            );
        END;

        IF OBJECT_ID(N'dbo.mo_production_events', N'U') IS NULL
        BEGIN
            CREATE TABLE mo_production_events (
                id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                manufacturing_order_id INT NOT NULL,
                operation_no INT NULL,
                packet_no NVARCHAR(50) NULL,
                actual_start DATETIME2 NULL,
                actual_end DATETIME2 NULL,
                planned_start DATETIME2 NULL,
                planned_end DATETIME2 NULL,
                planned_quantity FLOAT NULL,
                actual_good_quantity FLOAT NOT NULL DEFAULT 0,
                actual_rejected_quantity FLOAT NOT NULL DEFAULT 0,
                remaining_quantity FLOAT NULL,
                duration_seconds FLOAT NULL,
                duration_kind NVARCHAR(30) NULL,
                data_source NVARCHAR(40) NOT NULL DEFAULT N'MONTEPULL_REAL',
                import_batch_id BIGINT NULL,
                row_hash NVARCHAR(64) NOT NULL,
                source_sheet NVARCHAR(120) NULL,
                source_row_no INT NULL,
                created_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
                is_active BIT NOT NULL DEFAULT 1,
                FOREIGN KEY (manufacturing_order_id) REFERENCES manufacturing_orders(id),
                CONSTRAINT uq_mo_production_events_hash UNIQUE (row_hash)
            );
        END;

        IF OBJECT_ID(N'dbo.mp_dataset_config', N'U') IS NULL
        BEGIN
            CREATE TABLE mp_dataset_config (
                id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                active_dataset NVARCHAR(40) NOT NULL DEFAULT N'DEMO',
                updated_at DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
                updated_by NVARCHAR(100) NULL
            );
            INSERT INTO mp_dataset_config (active_dataset) VALUES (N'DEMO');
        END;

        IF COL_LENGTH('sales_order_lines', 'qty_launched') IS NULL
            ALTER TABLE sales_order_lines ADD qty_launched FLOAT NULL;
        IF COL_LENGTH('sales_order_lines', 'qty_produced') IS NULL
            ALTER TABLE sales_order_lines ADD qty_produced FLOAT NULL;
        IF COL_LENGTH('sales_order_lines', 'last_operation') IS NULL
            ALTER TABLE sales_order_lines ADD last_operation INT NULL;
        IF COL_LENGTH('sales_order_lines', 'remaining_to_launch') IS NULL
            ALTER TABLE sales_order_lines ADD remaining_to_launch FLOAT NULL;
        IF COL_LENGTH('sales_order_lines', 'remaining_to_produce') IS NULL
            ALTER TABLE sales_order_lines ADD remaining_to_produce FLOAT NULL;
        IF COL_LENGTH('sales_order_lines', 'delivery_date') IS NULL
            ALTER TABLE sales_order_lines ADD delivery_date DATE NULL;
        IF COL_LENGTH('sales_order_lines', 'data_source') IS NULL
            ALTER TABLE sales_order_lines ADD data_source NVARCHAR(40) NULL;
        IF COL_LENGTH('sales_order_lines', 'import_batch_id') IS NULL
            ALTER TABLE sales_order_lines ADD import_batch_id BIGINT NULL;

        IF COL_LENGTH('manufacturing_orders', 'planned_end') IS NULL
            ALTER TABLE manufacturing_orders ADD planned_end DATE NULL;
        IF COL_LENGTH('manufacturing_orders', 'actual_start') IS NULL
            ALTER TABLE manufacturing_orders ADD actual_start DATETIME2 NULL;
        IF COL_LENGTH('manufacturing_orders', 'actual_end') IS NULL
            ALTER TABLE manufacturing_orders ADD actual_end DATETIME2 NULL;
        IF COL_LENGTH('manufacturing_orders', 'qty_good') IS NULL
            ALTER TABLE manufacturing_orders ADD qty_good FLOAT NULL;
        IF COL_LENGTH('manufacturing_orders', 'qty_rejected') IS NULL
            ALTER TABLE manufacturing_orders ADD qty_rejected FLOAT NULL;
        IF COL_LENGTH('manufacturing_orders', 'delivery_date') IS NULL
            ALTER TABLE manufacturing_orders ADD delivery_date DATE NULL;
        IF COL_LENGTH('manufacturing_orders', 'last_operation') IS NULL
            ALTER TABLE manufacturing_orders ADD last_operation INT NULL;
        IF COL_LENGTH('manufacturing_orders', 'order_code') IS NULL
            ALTER TABLE manufacturing_orders ADD order_code NVARCHAR(100) NULL;
        IF COL_LENGTH('manufacturing_orders', 'data_source') IS NULL
            ALTER TABLE manufacturing_orders ADD data_source NVARCHAR(40) NULL;
        IF COL_LENGTH('manufacturing_orders', 'import_batch_id') IS NULL
            ALTER TABLE manufacturing_orders ADD import_batch_id BIGINT NULL;
        IF COL_LENGTH('manufacturing_orders', 'is_active') IS NULL
            ALTER TABLE manufacturing_orders ADD is_active BIT NOT NULL CONSTRAINT DF_mo_is_active DEFAULT 1;

        IF COL_LENGTH('sales_orders', 'data_source') IS NULL
            ALTER TABLE sales_orders ADD data_source NVARCHAR(40) NULL;
        IF COL_LENGTH('sales_orders', 'import_batch_id') IS NULL
            ALTER TABLE sales_orders ADD import_batch_id BIGINT NULL;
        """;
}
