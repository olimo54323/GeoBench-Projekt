using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using NetTopologySuite.Geometries;

namespace GeoBench.Core.Data
{
    /// <summary>Pole obiektu: nazwa kolumny w bazie i wartość jako tekst.</summary>
    public sealed record FeatureField(string Name, string Value);

    /// <summary>Obiekt do podglądu: tabela, identyfikator, pola w kolejności kolumn i geometria w EPSG:2180.</summary>
    public sealed record FeatureRecord(string Table, string Id, IReadOnlyList<FeatureField> Fields, Geometry? Geometry);

    /// <summary>
    /// Pola i wartości encji według modelu EF (nazwy kolumn snake_case, kolejność jak w migracjach: klucz,
    /// potem właściwości w kolejności deklaracji) oraz formaty kopiowania do schowka.
    /// Wartości są formatowane niezależnie od ustawień regionalnych (kropka dziesiętna, daty ISO).
    /// Geometria jest opisana podsumowaniem (typ, części, wierzchołki, powierzchnia, obwód, obwiednia),
    /// nie pełnym WKT, który dla stref ma tysiące wierzchołków.
    /// </summary>
    public static class FeatureAttributes
    {
        /// <summary>Koniec wiersza w kopiowanym tekście (CRLF: Excel, Notatnik i edytory Windows).</summary>
        public const string LineBreak = "\r\n";

        public static FeatureRecord FromEntity<TEntity>(GeoBenchContext context, TEntity entity, string id) where TEntity : class
        {
            var type = context.Model.FindEntityType(typeof(TEntity))
                ?? throw new InvalidOperationException($"Typ {typeof(TEntity).Name} nie należy do modelu.");
            var table = type.GetTableName()!;
            var storeObject = StoreObjectIdentifier.Table(table, type.GetSchema());
            var declared = typeof(TEntity).GetProperties()
                .Select((p, index) => (p.Name, index))
                .ToDictionary(x => x.Name, x => x.index);

            var fields = new List<FeatureField>();
            Geometry? geometry = null;
            foreach (var property in type.GetProperties()
                         .Where(p => p.PropertyInfo != null)
                         .OrderBy(p => p.IsPrimaryKey() ? -1 : declared.GetValueOrDefault(p.Name, int.MaxValue)))
            {
                var name = property.GetColumnName(storeObject) ?? property.Name;
                var value = property.PropertyInfo!.GetValue(entity);
                if (value is Geometry g)
                {
                    geometry ??= g;
                    fields.AddRange(GeometryFields(name, g));
                }
                else
                {
                    fields.Add(new FeatureField(name, Format(value)));
                }
            }
            return new FeatureRecord(table, id, fields, geometry);
        }

        /// <summary>Nazwa tabeli encji w bazie (np. dzialka).</summary>
        public static string TableName<TEntity>(GeoBenchContext context) =>
            context.Model.FindEntityType(typeof(TEntity))?.GetTableName()
            ?? throw new InvalidOperationException($"Typ {typeof(TEntity).Name} nie należy do modelu.");

        /// <summary>Wartość jako jeden wiersz tekstu; null to pusty tekst.</summary>
        public static string Format(object? value) => value switch
        {
            null => "",
            string s => SingleLine(s),
            DateTime d => d.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            DateTimeOffset d => d.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture),
            bool b => b ? "true" : "false",
            double x => x.ToString("0.###", CultureInfo.InvariantCulture),
            float x => x.ToString("0.###", CultureInfo.InvariantCulture),
            IFormattable f => SingleLine(f.ToString(null, CultureInfo.InvariantCulture)),
            _ => SingleLine(value.ToString() ?? "")
        };

        /// <summary>Pole z wartością: <c>pole\twartość</c>.</summary>
        public static string CopyField(FeatureField field) => $"{field.Name}\t{field.Value}";

        /// <summary>Cały obiekt: wiersz <c>pole\twartość</c> dla każdego pola.</summary>
        public static string CopyRecord(FeatureRecord record) => string.Join(LineBreak, record.Fields.Select(CopyField));

        /// <summary>
        /// Tabela: wiersz nagłówka z nazwami pól, potem jeden wiersz na obiekt, wartości rozdzielone tabulatorem
        /// (wkleja się do arkusza jako tabela). Pole, którego obiekt nie ma (np. geometria null), jest puste.
        /// </summary>
        public static string CopyTable(IEnumerable<FeatureRecord> records)
        {
            var list = records.ToList();
            var names = new List<string>();
            foreach (var name in list.SelectMany(r => r.Fields).Select(f => f.Name))
            {
                if (!names.Contains(name))
                    names.Add(name);
            }

            var lines = new List<string> { string.Join("\t", names) };
            foreach (var record in list)
            {
                var values = new Dictionary<string, string>();
                foreach (var field in record.Fields)
                    values.TryAdd(field.Name, field.Value);
                lines.Add(string.Join("\t", names.Select(n => values.GetValueOrDefault(n, ""))));
            }
            return string.Join(LineBreak, lines);
        }

        private static IEnumerable<FeatureField> GeometryFields(string name, Geometry geometry)
        {
            var envelope = geometry.EnvelopeInternal;
            yield return new FeatureField(name, $"{geometry.GeometryType} (EPSG:{geometry.SRID})");
            yield return new FeatureField($"{name}.czesci", Format(geometry.NumGeometries));
            yield return new FeatureField($"{name}.wierzcholki", Format(geometry.NumPoints));
            yield return new FeatureField($"{name}.powierzchnia_m2", Metres(geometry.Area));
            yield return new FeatureField($"{name}.obwod_m", Metres(geometry.Length));
            yield return new FeatureField($"{name}.obwiednia",
                envelope.IsNull ? "" : $"{Metres(envelope.MinX)} {Metres(envelope.MinY)}, {Metres(envelope.MaxX)} {Metres(envelope.MaxY)}");
            yield return new FeatureField($"{name}.poprawna", Format(geometry.IsValid));
        }

        private static string Metres(double value) => value.ToString("0.00", CultureInfo.InvariantCulture);

        private static string SingleLine(string text) =>
            text.IndexOfAny(['\t', '\r', '\n']) < 0
                ? text
                : text.Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');
    }
}
