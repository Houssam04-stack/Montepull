namespace Axioplan.GammesNomenclatures.Infrastructure.Mvp0;

/// <summary>Convertit un fichier tabulaire (Excel, CSV, Word, PDF…) en CSV texte pour l'assistant MVP-0.</summary>
public static class Mvp0ExcelCsvConverter
{
    public static IReadOnlyList<(string SheetName, string Csv)> ToCsvSheets(byte[] fileContent, string? fileName = null)
        => Imports.UniversalWorkbookLoader.ToCsvSheets(fileContent, fileName);
}
