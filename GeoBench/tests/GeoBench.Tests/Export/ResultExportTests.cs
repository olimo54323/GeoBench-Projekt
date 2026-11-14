using System.Globalization;
using System.Text;
using System.Xml.Linq;
using ClosedXML.Excel;
using GeoBench.Core.Data;
using GeoBench.Core.Export;
using GeoBench.Core.Model;

namespace GeoBench.Tests.Export
{
    /// <summary>Eksport wyników bez bazy: kolumny z modelu, zapis CSV, XML i XLSX.</summary>
    public class ResultExportTests
    {
        // Model EF powstaje bez połączenia, więc wystarczą dowolne parametry połączenia
        private static GeoBenchContext SqlServer() => new SqlServerContext(SqlServerContext.CreateOptions("Server=.;Database=model"));

        private static GeoBenchContext Postgres() => new PostgresContext(PostgresContext.CreateOptions("Host=localhost;Database=model"));

        private static readonly ResultTable Nearest = ResultTableCatalog.All.Single(t => t.EntityType == typeof(BudynekNajblizszy));

        private static ExportData Sample() => new(
            [new ResultColumn("id_budynku", typeof(string)), new ResultColumn("odleglosc_m", typeof(double)), new ResultColumn("liczba", typeof(int))],
            [
                ["b1;2", 0.1, 3],
                ["b\"3", 1234.5, 0],
                ["b4", null, 7]
            ]);

        private static readonly ExportInfo Info = new("budynki_najblizsze", "W4P", "PostgreSQL + PostGIS", new DateTime(2026, 11, 14, 12, 30, 0));

        [Fact]
        public void Columns_FollowEntityOrder_WithoutKey()
        {
            using var context = SqlServer();

            var columns = ResultTableCatalog.Columns(context, Nearest);

            Assert.Equal(new[] { "wariant", "id_budynku", "id_sasiada", "odleglosc_m" }, columns.Select(c => c.Name));
            Assert.Equal(new[] { false, false, false, true }, columns.Select(c => c.IsNumeric));
            Assert.Equal("budynki_najblizsze", ResultTableCatalog.TableName(context, Nearest));
        }

        [Fact]
        public void Catalog_AllTablesHaveVariantColumn_InBothModels()
        {
            using var sqlServer = SqlServer();
            using var postgres = Postgres();

            foreach (var table in ResultTableCatalog.All)
            {
                Assert.Contains(ResultTableCatalog.Columns(sqlServer, table), c => c.Name == ResultTableCatalog.VariantColumn);
                Assert.Equal(ResultTableCatalog.TableName(sqlServer, table), ResultTableCatalog.TableName(postgres, table));
            }
            Assert.Equal(7, ResultTableCatalog.All.Count);
        }

        [Fact]
        public void Csv_EscapesAndUsesInvariantNumbers()
        {
            var previous = CultureInfo.CurrentCulture;
            CultureInfo.CurrentCulture = new CultureInfo("pl-PL");
            try
            {
                using var stream = new MemoryStream();
                ResultWriters.WriteCsv(Sample(), stream);

                var bytes = stream.ToArray();
                Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]);
                var lines = Encoding.UTF8.GetString(bytes[3..]).ReplaceLineEndings("\n").TrimEnd('\n').Split('\n');
                Assert.Equal(new[]
                {
                    "id_budynku;odleglosc_m;liczba",
                    "\"b1;2\";0.1;3",
                    "\"b\"\"3\";1234.5;0",
                    "b4;;7"
                }, lines);
            }
            finally
            {
                CultureInfo.CurrentCulture = previous;
            }
        }

        [Fact]
        public void Xml_HasRowPerRecord_AndDescription()
        {
            using var stream = new MemoryStream();
            ResultWriters.WriteXml(Sample(), Info, stream);

            stream.Position = 0;
            var root = XDocument.Load(stream).Root!;
            Assert.Equal("wyniki", root.Name.LocalName);
            Assert.Equal("W4P", (string?)root.Attribute("wariant"));
            Assert.Equal("3", (string?)root.Attribute("wierszy"));
            var rows = root.Elements("wiersz").ToList();
            Assert.Equal(3, rows.Count);
            Assert.Equal("b1;2", (string?)rows[0].Element("id_budynku"));
            Assert.Equal("1234.5", (string?)rows[1].Element("odleglosc_m"));
            Assert.Null(rows[2].Element("odleglosc_m"));
        }

        [Fact]
        public void Xlsx_HasHeaderNumbersAndInfoSheet()
        {
            using var stream = new MemoryStream();
            ResultWriters.WriteXlsx(Sample(), Info, stream);

            stream.Position = 0;
            using var workbook = new XLWorkbook(stream);
            var sheet = workbook.Worksheet("budynki_najblizsze");
            Assert.Equal("id_budynku", sheet.Cell(1, 1).GetString());
            Assert.Equal("b\"3", sheet.Cell(3, 1).GetString());
            Assert.Equal(1234.5, sheet.Cell(3, 2).GetDouble());
            Assert.Equal(7, sheet.Cell(4, 3).GetDouble());
            Assert.True(sheet.Cell(4, 2).IsEmpty());
            Assert.Equal("W4P", workbook.Worksheet("info").Cell(2, 2).GetString());
        }

        [Theory]
        [InlineData(ExportFormat.Xlsx, ".xlsx")]
        [InlineData(ExportFormat.Csv, ".csv")]
        [InlineData(ExportFormat.Xml, ".xml")]
        public void Extension_MatchesFormat(ExportFormat format, string extension)
        {
            Assert.Equal(extension, ResultWriters.Extension(format));
            Assert.EndsWith(extension, ResultWriters.DialogFilter(format));
        }
    }
}
