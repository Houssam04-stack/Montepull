using Axioplan.GammesNomenclatures.Application.Models;
using Axioplan.GammesNomenclatures.Infrastructure.Data;
using Axioplan.GammesNomenclatures.Infrastructure.Logging;
using Axioplan.GammesNomenclatures.Infrastructure.Repositories;
using Microsoft.Extensions.Options;
using Xunit;

namespace Axioplan.GammesNomenclatures.Domain.Tests;

public sealed class ExcelImportAnalysisTests
{
    private const string OrderFilePath = @"C:\Users\USER\Downloads\DOUBLYGILF.xlsx";
    private const string BomFilePath = @"C:\Users\USER\Downloads\Nomenclature DOULBYGILF.xls";

    [Fact]
    public async Task AnalyzeOrderWorkbook_DetectsSheetAndKeyMappings()
    {
        if (!File.Exists(OrderFilePath))
        {
            return;
        }

        var repository = CreateRepository();
        var bytes = await File.ReadAllBytesAsync(OrderFilePath);

        var result = await repository.AnalyzeOrderWorkbookAsync(bytes, Path.GetFileName(OrderFilePath));

        Assert.Equal(ImportTargets.Order, result.ImportTarget);
        Assert.NotEmpty(result.Sheets);
        var sheet = result.Sheets[0];
        Assert.True(sheet.SuggestedMetadataLabelRow > 0);
        Assert.True(sheet.SuggestedQuantityRow > 0);
        Assert.Contains(sheet.SuggestedMappings, mapping => mapping.FieldKey == ImportFieldKeys.OrderReference && mapping.ColumnIndex is not null);
    }

    [Fact]
    public async Task AnalyzeBomWorkbook_DetectsHeaderAndDesignationColumn()
    {
        if (!File.Exists(BomFilePath))
        {
            return;
        }

        var repository = CreateRepository();
        var bytes = await File.ReadAllBytesAsync(BomFilePath);

        var result = await repository.AnalyzeBomWorkbookAsync(bytes, Path.GetFileName(BomFilePath));

        Assert.Equal(ImportTargets.Bom, result.ImportTarget);
        Assert.NotEmpty(result.Sheets);
        var sheet = result.Sheets[0];
        Assert.NotEmpty(sheet.SuggestedHeaderRows);
        Assert.Contains(sheet.SuggestedMappings, mapping => mapping.FieldKey == ImportFieldKeys.BomDesignation && mapping.ColumnIndex is not null);
        Assert.True(sheet.SuggestedDataStartRow > 0);
        Assert.NotEmpty(sheet.PreviewRows);
        Assert.True(sheet.PreviewRows.Count > 20);
        Assert.NotNull(sheet.ExtractedPreviewRows);
        Assert.NotEmpty(sheet.ExtractedPreviewRows!);
        Assert.Contains(sheet.ExtractedPreviewRows!, row => row.Designation == "Sachet");
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
