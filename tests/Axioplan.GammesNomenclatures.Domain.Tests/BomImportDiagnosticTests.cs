using Axioplan.GammesNomenclatures.Application.Models;
using Axioplan.GammesNomenclatures.Infrastructure.Data;
using Axioplan.GammesNomenclatures.Infrastructure.Logging;
using Axioplan.GammesNomenclatures.Infrastructure.Repositories;
using Microsoft.Extensions.Options;
using Xunit;
using Xunit.Abstractions;

namespace Axioplan.GammesNomenclatures.Domain.Tests;

public sealed class BomImportDiagnosticTests(ITestOutputHelper output)
{
    private const string BomFilePath = @"C:\Users\USER\Downloads\Nomenclature DOULBYGILF.xls";

    [Fact]
    public async Task DiagnoseRealBomFile_ListsExtractedFournitures()
    {
        if (!File.Exists(BomFilePath))
        {
            return;
        }

        var repository = CreateRepository();
        var bytes = await File.ReadAllBytesAsync(BomFilePath);
        var result = await repository.AnalyzeBomWorkbookAsync(bytes, Path.GetFileName(BomFilePath));
        var sheet = result.Sheets[0];

        output.WriteLine($"Sheet={sheet.SheetName} rows={sheet.RowCount} header={sheet.SuggestedHeaderRows.FirstOrDefault()} extracted={sheet.ExtractedPreviewRows?.Count}");
        foreach (var mapping in sheet.SuggestedMappings)
        {
            output.WriteLine($"MAP {mapping.FieldKey} col={mapping.ColumnIndex} header={mapping.SourceHeader}");
        }

        foreach (var row in sheet.ExtractedPreviewRows ?? [])
        {
            output.WriteLine($"ROW {row.SourceRowNumber} {row.Designation} status={row.Status}");
        }

        Assert.NotNull(sheet.ExtractedPreviewRows);
        Assert.NotEmpty(sheet.ExtractedPreviewRows!);
        Assert.Contains(sheet.ExtractedPreviewRows!, row => row.Designation?.Contains("Vignette", StringComparison.OrdinalIgnoreCase) == true);
        Assert.Contains(sheet.ExtractedPreviewRows!, row => row.Designation?.Contains("Sachet", StringComparison.OrdinalIgnoreCase) == true);
    }

    private static SqlServerImportRepository CreateRepository()
    {
        var databaseOptions = Options.Create(new DatabaseOptions
        {
            ConnectionString = "Server=(localdb)\\MSSQLLocalDB;Integrated Security=true;Initial Catalog=unused"
        });

        return new SqlServerImportRepository(databaseOptions, new SqlApplicationLogger(databaseOptions));
    }
}
