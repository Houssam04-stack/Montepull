using System.Data;
using System.Globalization;
using System.Text;
using ExcelDataReader;

namespace Axioplan.GammesNomenclatures.Infrastructure.Mvp0;

/// <summary>Convertit un classeur Excel Montepull en CSV texte (1ère feuille ou feuille choisie) pour l'assistant MVP-0.</summary>
public static class Mvp0ExcelCsvConverter
{
    public static IReadOnlyList<(string SheetName, string Csv)> ToCsvSheets(byte[] fileContent)
    {
        if (fileContent is null || fileContent.Length == 0)
            throw new InvalidOperationException("Fichier Excel vide.");

        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        try
        {
            using var stream = new MemoryStream(fileContent);
            using var reader = ExcelReaderFactory.CreateReader(stream);
            var dataSet = reader.AsDataSet(new ExcelDataSetConfiguration { UseColumnDataType = false });
            if (dataSet.Tables.Count == 0)
                throw new InvalidOperationException("Aucune feuille dans le classeur.");

            var list = new List<(string, string)>();
            foreach (DataTable table in dataSet.Tables)
            {
                var sb = new StringBuilder();
                for (var r = 0; r < table.Rows.Count; r++)
                {
                    var cells = new List<string>();
                    for (var c = 0; c < table.Columns.Count; c++)
                        cells.Add(Escape(CellText(table.Rows[r][c])));
                    // ignorer lignes totalement vides en fin
                    if (cells.All(string.IsNullOrWhiteSpace)) continue;
                    sb.AppendLine(string.Join(';', cells));
                }

                if (sb.Length > 0)
                    list.Add((table.TableName, sb.ToString()));
            }

            if (list.Count == 0)
                throw new InvalidOperationException("Toutes les feuilles Excel sont vides.");

            return list;
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Lecture Excel impossible (.xlsx/.xls). " + ex.Message, ex);
        }
    }

    private static string CellText(object? value) => value switch
    {
        null or DBNull => string.Empty,
        DateTime d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        double n when Math.Abs(n % 1) < double.Epsilon => n.ToString("0", CultureInfo.InvariantCulture),
        double n => n.ToString("0.###", CultureInfo.InvariantCulture),
        _ => value.ToString()?.Trim() ?? string.Empty
    };

    private static string Escape(string value)
    {
        if (value.Contains(';') || value.Contains('"') || value.Contains('\n'))
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        return value;
    }
}
