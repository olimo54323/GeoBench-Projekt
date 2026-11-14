using System.Globalization;
using System.Text;
using System.Xml;
using ClosedXML.Excel;

namespace GeoBench.Core.Export
{
    /// <summary>Format pliku eksportu.</summary>
    public enum ExportFormat
    {
        Xlsx,
        Csv,
        Xml
    }

    /// <summary>Opis eksportu zapisywany razem z danymi (atrybuty XML, arkusz „info” w XLSX).</summary>
    public sealed record ExportInfo(string Table, string Variant, string Database, DateTime ExportedAt);

    /// <summary>
    /// Zapis danych eksportu do pliku. Liczby niezależnie od ustawień regionalnych: w CSV i XML z kropką,
    /// w XLSX jako liczby (formatowanie przejmuje Excel).
    /// </summary>
    public static class ResultWriters
    {
        /// <summary>Limit wierszy arkusza Excela (z nagłówkiem).</summary>
        public const int XlsxMaxRows = 1_048_576;

        /// <summary>Do tylu wierszy dopasowywana jest szerokość kolumn XLSX (dalej tylko koszt czasu).</summary>
        private const int AutoFitRows = 1000;

        public static string Extension(ExportFormat format) => format switch
        {
            ExportFormat.Xlsx => ".xlsx",
            ExportFormat.Csv => ".csv",
            _ => ".xml"
        };

        public static string DialogFilter(ExportFormat format) => format switch
        {
            ExportFormat.Xlsx => "Skoroszyt Excel (*.xlsx)|*.xlsx",
            ExportFormat.Csv => "CSV rozdzielany średnikami (*.csv)|*.csv",
            _ => "XML (*.xml)|*.xml"
        };

        public static void Write(ExportFormat format, ExportData data, ExportInfo info, Stream stream)
        {
            switch (format)
            {
                case ExportFormat.Xlsx:
                    WriteXlsx(data, info, stream);
                    break;
                case ExportFormat.Csv:
                    WriteCsv(data, stream);
                    break;
                default:
                    WriteXml(data, info, stream);
                    break;
            }
        }

        /// <summary>CSV: UTF-8 z BOM (Excel rozpoznaje polskie znaki), separator średnik, jak summary.csv raportów.</summary>
        public static void WriteCsv(ExportData data, Stream stream)
        {
            using var writer = new StreamWriter(stream, new UTF8Encoding(true), leaveOpen: true);
            writer.WriteLine(string.Join(';', data.Columns.Select(c => Escape(c.Name))));
            foreach (var row in data.Rows)
                writer.WriteLine(string.Join(';', row.Select(v => Escape(Text(v)))));
        }

        /// <summary>XML: element wiersz na rekord, element na kolumnę (nazwy kolumn snake_case są poprawnymi nazwami XML).</summary>
        public static void WriteXml(ExportData data, ExportInfo info, Stream stream)
        {
            var settings = new XmlWriterSettings { Indent = true, Encoding = new UTF8Encoding(false) };
            using var writer = XmlWriter.Create(stream, settings);
            writer.WriteStartDocument();
            writer.WriteStartElement("wyniki");
            writer.WriteAttributeString("tabela", info.Table);
            writer.WriteAttributeString("wariant", info.Variant);
            writer.WriteAttributeString("baza", info.Database);
            writer.WriteAttributeString("wyeksportowano", info.ExportedAt.ToString("s", CultureInfo.InvariantCulture));
            writer.WriteAttributeString("wierszy", data.Rows.Count.ToString(CultureInfo.InvariantCulture));
            foreach (var row in data.Rows)
            {
                writer.WriteStartElement("wiersz");
                for (var i = 0; i < data.Columns.Count; i++)
                {
                    if (row[i] == null)
                        continue;
                    writer.WriteElementString(XmlConvert.EncodeLocalName(data.Columns[i].Name), Text(row[i]));
                }
                writer.WriteEndElement();
            }
            writer.WriteEndElement();
            writer.WriteEndDocument();
        }

        /// <summary>XLSX: arkusz z danymi (pogrubiony, zamrożony nagłówek, autofiltr) i arkusz info z opisem eksportu.</summary>
        public static void WriteXlsx(ExportData data, ExportInfo info, Stream stream)
        {
            if (data.Rows.Count + 1 > XlsxMaxRows)
                throw new InvalidOperationException(
                    $"Arkusz Excela mieści {XlsxMaxRows - 1:N0} wierszy danych, a wariant ma {data.Rows.Count:N0}. Wybierz CSV albo XML.");

            using var workbook = new XLWorkbook();
            var sheet = workbook.Worksheets.Add(SheetName(info.Table));
            for (var c = 0; c < data.Columns.Count; c++)
                sheet.Cell(1, c + 1).Value = data.Columns[c].Name;
            for (var r = 0; r < data.Rows.Count; r++)
            {
                var row = data.Rows[r];
                for (var c = 0; c < row.Length; c++)
                    SetValue(sheet.Cell(r + 2, c + 1), row[c]);
            }

            sheet.Row(1).Style.Font.Bold = true;
            sheet.SheetView.FreezeRows(1);
            if (data.Columns.Count > 0)
            {
                sheet.Range(1, 1, data.Rows.Count + 1, data.Columns.Count).SetAutoFilter();
                sheet.Columns(1, data.Columns.Count).AdjustToContents(1, Math.Min(data.Rows.Count + 1, AutoFitRows));
            }

            var about = workbook.Worksheets.Add("info");
            var lines = new (string Name, string Value)[]
            {
                ("tabela", info.Table), ("wariant", info.Variant), ("baza", info.Database),
                ("wyeksportowano", info.ExportedAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)),
                ("wierszy", data.Rows.Count.ToString(CultureInfo.InvariantCulture))
            };
            for (var i = 0; i < lines.Length; i++)
            {
                about.Cell(i + 1, 1).Value = lines[i].Name;
                about.Cell(i + 1, 2).Value = lines[i].Value;
            }
            about.Column(1).Style.Font.Bold = true;
            about.Columns(1, 2).AdjustToContents();

            workbook.SaveAs(stream);
        }

        private static void SetValue(IXLCell cell, object? value)
        {
            switch (value)
            {
                case null:
                    break;
                case string s:
                    cell.Value = s;
                    break;
                case double d:
                    cell.Value = d;
                    break;
                case float f:
                    cell.Value = (double)f;
                    break;
                case decimal m:
                    cell.Value = (double)m;
                    break;
                case int i:
                    cell.Value = i;
                    break;
                case long l:
                    cell.Value = (double)l;
                    break;
                case short sh:
                    cell.Value = (int)sh;
                    break;
                case bool b:
                    cell.Value = b;
                    break;
                case DateTime dt:
                    cell.Value = dt;
                    break;
                default:
                    cell.Value = Text(value);
                    break;
            }
        }

        /// <summary>Nazwa arkusza: do 31 znaków, bez znaków niedozwolonych w Excelu.</summary>
        private static string SheetName(string table)
        {
            var name = new string(table.Select(ch => "[]:*?/\\".Contains(ch) ? '_' : ch).ToArray());
            return name.Length > 31 ? name[..31] : name;
        }

        /// <summary>Wartość jako tekst niezależnie od ustawień regionalnych (liczby z kropką, najkrótszy zapis dokładny).</summary>
        public static string Text(object? value) => value switch
        {
            null => "",
            string s => s,
            double d => d.ToString("R", CultureInfo.InvariantCulture),
            float f => f.ToString("R", CultureInfo.InvariantCulture),
            DateTime dt => dt.ToString("s", CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? ""
        };

        private static string Escape(string field) =>
            field.IndexOfAny([';', '"', '\n', '\r']) >= 0 ? $"\"{field.Replace("\"", "\"\"")}\"" : field;
    }
}
