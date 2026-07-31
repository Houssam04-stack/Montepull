using Axioplan.GammesNomenclatures.Infrastructure.Data;
using Axioplan.GammesNomenclatures.Infrastructure.Logging;
using Axioplan.GammesNomenclatures.Infrastructure.Repositories;
using Microsoft.Extensions.Options;

var connectionString =
    Environment.GetEnvironmentVariable("AXIOPLAN_CONNECTION_STRING_DOTNET")
    ?? "Server=localhost\\SQLEXPRESS;Database=AxioplanMvp;Trusted_Connection=True;TrustServerCertificate=True;";

var files = args.Length > 0
    ? args.ToList()
    : new List<string>
    {
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "commandes.xlsx"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "ListeSuivi_2026-07-22.xlsx"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "SuiviOperations_2026-07-22.xlsx"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "DOUBLYGILF.xlsx"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "Nomenclature DOULBYGILF.xlsx"),
    };

if (!File.Exists(files[^1]))
{
    var xls = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "Nomenclature DOULBYGILF.xls");
    if (File.Exists(xls))
        files[^1] = xls;
}

foreach (var f in files)
{
    if (!File.Exists(f))
    {
        Console.Error.WriteLine($"Fichier manquant : {f}");
        return 1;
    }
}

var opts = Options.Create(new DatabaseOptions { ConnectionString = connectionString });
var repo = new SqlServerMontepullImportRepository(opts, new SqlApplicationLogger(opts));
Console.WriteLine("EnsureSchema...");
await repo.EnsureSchemaAsync();

var payloads = new List<(byte[] Content, string FileName)>();
foreach (var f in files)
{
    Console.WriteLine($"Lecture {Path.GetFileName(f)} ({new FileInfo(f).Length:N0} octets)...");
    payloads.Add((await File.ReadAllBytesAsync(f), Path.GetFileName(f)));
}

Console.WriteLine("Staging...");
var staged = await repo.StageFilesAsync(payloads, "cli", "REAL_IMPORT");
Console.WriteLine($"Batch {staged.BatchId} status={staged.Status} read={staged.LinesRead} valid={staged.LinesValid} warn={staged.LinesWarning} rejected={staged.LinesRejected}");
Console.WriteLine($"CMD={staged.CommandesDetected} OF={staged.OfDetected} OPS={staged.OperationsDetected} ART={staged.ArticlesDetected} DUP={staged.DuplicatesDetected}");
Console.WriteLine($"Anomalies: {staged.Anomalies.Count}");
foreach (var a in staged.Anomalies.Take(20))
    Console.WriteLine($"  [{a.Severity}] {a.Code}: {a.Message}");

Console.WriteLine("Validate...");
var validated = await repo.ValidateBatchAsync(staged.BatchId);
Console.WriteLine($"Validate status={validated.Status} anomalies={validated.Anomalies.Count}");

Console.WriteLine("Promote...");
var promoted = await repo.PromoteBatchAsync(staged.BatchId);
Console.WriteLine($"Promote status={promoted.Status}");

await repo.SetActiveDatasetAsync("MONTEPULL_REAL", "cli");
var ofList = await repo.ListImportedManufacturingOrdersAsync();
Console.WriteLine($"OF importés actifs: {ofList.Count}");
foreach (var of in ofList.Take(10))
    Console.WriteLine($"  {of.OfCode} art={of.ArticleCode} qty={of.Qty} status={of.Status} cmd={of.OrderCode}");

Console.WriteLine("OK");
return 0;
