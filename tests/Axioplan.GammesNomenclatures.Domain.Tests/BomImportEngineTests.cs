using Axioplan.GammesNomenclatures.Application.Models;
using Axioplan.GammesNomenclatures.Infrastructure.Imports;
using Xunit;

namespace Axioplan.GammesNomenclatures.Domain.Tests;

public sealed class BomImportEngineTests
{
    [Fact]
    public void ListUnmappedHeaders_ReportsUnknownColumnsExplicitly()
    {
        var headers = new[] { "Fournitures", "Reference", "Colonne Mystere", "Besoin" };
        var mappings = new List<ImportFieldMapping>
        {
            new(ImportFieldKeys.BomDesignation, 0, "Fournitures", true),
            new(ImportFieldKeys.BomReference, 1, "Reference", true),
            new(ImportFieldKeys.BomNeed, 3, "Besoin", true),
            new(ImportFieldKeys.BomUsage, null, null, false),
            new(ImportFieldKeys.BomSupplier, null, null, false),
            new(ImportFieldKeys.BomUnitPrice, null, null, false),
            new(ImportFieldKeys.BomPlacement, null, null, false)
        };

        var unknown = BomImportEngine.ListUnmappedHeaders(headers, mappings);
        Assert.Contains(unknown, u => u.Contains("Colonne Mystere", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(unknown, u => u.Contains("Fournitures", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void DetectHeaderRow_FindsMontepullHeader()
    {
        var rows = BuildMontepullSampleRows();
        var sheet = BomImportEngine.NormalizeSheet(rows);
        var headerRow = BomImportEngine.DetectHeaderRow(sheet);

        Assert.Equal(15, headerRow);
    }

    [Fact]
    public void DetectColumnMappings_UsesHeaderLabelsNotFixedPositions()
    {
        var rows = BuildMontepullSampleRows();
        var sheet = BomImportEngine.NormalizeSheet(rows);
        var mappings = BomImportEngine.DetectColumnMappings(sheet, 15);

        Assert.Equal(0, mappings.First(m => m.FieldKey == ImportFieldKeys.BomDesignation).ColumnIndex);
        Assert.Equal(3, mappings.First(m => m.FieldKey == ImportFieldKeys.BomReference).ColumnIndex);
        Assert.Equal(5, mappings.First(m => m.FieldKey == ImportFieldKeys.BomUsage).ColumnIndex);
        Assert.Equal(6, mappings.First(m => m.FieldKey == ImportFieldKeys.BomNeed).ColumnIndex);
    }

    [Fact]
    public void ExtractRows_KeepsAmbiguousLinesWithConfirmationStatus()
    {
        var rows = BuildMontepullSampleRows();
        var sheet = BomImportEngine.NormalizeSheet(rows);
        var mappings = BomImportEngine.DetectColumnMappings(sheet, 15);
        var warnings = new List<string>();

        var extracted = BomImportEngine.ExtractRows(sheet, 15, 16, mappings, warnings);

        Assert.Contains(extracted, row => row.Designation == "Sachet" && row.Status == "A_CONFIRMER");
        Assert.Contains(extracted, row => row.Designation == "BOUTON RECOUVERT");
        Assert.DoesNotContain(extracted, row => row.Designation == "Commentaires");
        Assert.True(extracted.Count >= 4);
    }

    [Fact]
    public void NormalizeSheet_ForwardFillsMergedReferenceColumn()
    {
        var rows = new List<IReadOnlyList<string>>
        {
            new[] { "FOURNITURES", "", "Reference", "Emploi", "Besoin" },
            new[] { "Composant A", "", "REF-001", "1", "" },
            new[] { "Composant A", "", "", "2", "" }
        };

        var sheet = BomImportEngine.NormalizeSheet(rows);
        var mappings = BomImportEngine.DetectColumnMappings(sheet, 1);
        var warnings = new List<string>();
        var extracted = BomImportEngine.ExtractRows(sheet, 1, 2, mappings, warnings);

        Assert.Equal(2, extracted.Count);
        Assert.Equal("REF-001", extracted[1].Reference);
        Assert.Equal("Composant A", extracted[1].Designation);
    }

    private static List<IReadOnlyList<string>> BuildMontepullSampleRows()
    {
        var rows = new List<IReadOnlyList<string>>();
        for (var index = 0; index < 14; index++)
        {
            rows.Add(index == 1 ? new[] { "FICHE NOMENCLATURE", "", "", "", "", "", "", "", "", "" } : new string[10]);
        }

        rows.Add(new[] { "FOURNITURES", "", "", "Reference", "Lg/e", "Emploi", "Besoin", "Fournisseur", "Prix", "Echantillon" });
        rows.Add(new[] { "Vignette de composition", "", "", "", "", "2", "", "SML", "0.2676", "" });
        rows.Add(new[] { "RFID", "", "", "", "", "1", "", "SML", "0.8028", "" });
        rows.Add(new[] { "Sachet", "", "", "", "", "1", "A VERIFIER", "MARPLAST", "0.56", "15" });
        rows.Add(new[] { "BOUTON RECOUVERT", "", "", "L32", "", "", "", "SOURCING PLUS", "", "" });
        rows.Add(new[] { "", "", "", "", "", "", "", "", "", "15" });
        rows.Add(new[] { "Commentaires", "", "", "", "", "", "", "", "", "" });
        return rows;
    }
}
