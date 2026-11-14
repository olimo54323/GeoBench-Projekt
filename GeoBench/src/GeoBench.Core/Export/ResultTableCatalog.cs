using System.Reflection;
using GeoBench.Core.Data;
using GeoBench.Core.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace GeoBench.Core.Export
{
    /// <summary>Kolumna tabeli wyników: nazwa w bazie i typ CLR właściwości (liczby zapisywane jako liczby).</summary>
    public sealed record ResultColumn(string Name, Type ClrType)
    {
        public bool IsNumeric => (Nullable.GetUnderlyingType(ClrType) ?? ClrType) is var t
            && (t == typeof(double) || t == typeof(float) || t == typeof(decimal) || t == typeof(int) || t == typeof(long) || t == typeof(short));
    }

    /// <summary>Tabela wyników scenariusza do eksportu: encja modelu i opis do listy.</summary>
    public sealed record ResultTable(Type EntityType, string Title);

    /// <summary>
    /// Tabele wyników scenariuszy (wiersze z kolumną wariant). Nazwy tabel i kolumn pochodzą z modelu EF,
    /// więc obowiązuje ta sama konwencja co w migracjach; kolejność kolumn jak w klasie encji, bez klucza oid.
    /// </summary>
    public static class ResultTableCatalog
    {
        public static IReadOnlyList<ResultTable> All { get; } =
        [
            new(typeof(BudynekStrefa), "04 – budynek (centroid) w strefie"),
            new(typeof(DzialkaStrefaPow), "05a – działka × strefa, pole przecięcia"),
            new(typeof(BudynekStrefaPow), "05b – budynek × strefa, pole przecięcia"),
            new(typeof(BudynekDzialkaPow), "05c – działka × budynek, pole przecięcia"),
            new(typeof(BudynekNajblizszy), "06 – najbliższy budynek"),
            new(typeof(DzialkaBudynki50m), "07 – budynki do 50 m od działki"),
            new(typeof(GeometriaNaprawiona), "08 – geometrie naprawione")
        ];

        /// <summary>Kolumna wariantu (wspólna dla wszystkich tabel wyników).</summary>
        public const string VariantColumn = "wariant";

        public static string TableName(GeoBenchContext context, ResultTable table) =>
            Entity(context, table).GetTableName()!;

        /// <summary>Kolumny tabeli w kolejności właściwości klasy encji, bez klucza głównego.</summary>
        public static IReadOnlyList<ResultColumn> Columns(GeoBenchContext context, ResultTable table)
        {
            var entity = Entity(context, table);
            var store = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
            var columns = new List<ResultColumn>();
            foreach (var info in table.EntityType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                var property = entity.FindProperty(info.Name);
                if (property == null || property.IsPrimaryKey())
                    continue;
                columns.Add(new ResultColumn(property.GetColumnName(store)!, property.ClrType));
            }
            return columns;
        }

        private static IEntityType Entity(GeoBenchContext context, ResultTable table) =>
            context.Model.FindEntityType(table.EntityType)
            ?? throw new InvalidOperationException($"Typ {table.EntityType.Name} nie należy do modelu.");
    }
}
