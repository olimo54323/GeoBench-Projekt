using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace GeoBench.Core.Data.Filters
{
    public enum WhereColumnKind
    {
        Text,
        Integer,
        Decimal,
        Date,
        DateTime,
        Enum
    }

    /// <summary>Kolumna dostępna w warunku: nazwa w bazie, rodzaj wartości i właściwość encji.</summary>
    public sealed class WhereColumn
    {
        internal WhereColumn(string name, WhereColumnKind kind, PropertyInfo property, bool isNullable, bool isUtc)
        {
            Name = name;
            Kind = kind;
            Property = property;
            IsNullable = isNullable;
            IsUtc = isUtc;
            ValueType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
            EnumValues = ValueType.IsEnum ? Enum.GetNames(ValueType) : [];
        }

        public string Name { get; }

        public WhereColumnKind Kind { get; }

        public PropertyInfo Property { get; }

        /// <summary>Typ wartości bez Nullable.</summary>
        public Type ValueType { get; }

        public bool IsNullable { get; }

        /// <summary>Kolumna daty ze strefą (timestamptz): wartości z warunku traktowane jako UTC.</summary>
        public bool IsUtc { get; }

        /// <summary>Dozwolone wartości kolumny wyliczeniowej (np. rodzaj strefy).</summary>
        public IReadOnlyList<string> EnumValues { get; }

        public string KindName => Kind switch
        {
            WhereColumnKind.Text => "tekst",
            WhereColumnKind.Integer => "liczba całkowita",
            WhereColumnKind.Decimal => "liczba",
            WhereColumnKind.Date => "data",
            WhereColumnKind.DateTime => "data i czas",
            _ => "lista wartości"
        };
    }

    /// <summary>
    /// Kolumny tabeli dostępne w warunku WHERE, z modelu EF (nazwy snake_case jak w bazie, kolejność jak w migracjach).
    /// Geometria i typy bez odpowiednika w składni są pomijane.
    /// </summary>
    public sealed class WhereSchema
    {
        private WhereSchema(string table, Type entityType, IReadOnlyList<WhereColumn> columns)
        {
            Table = table;
            EntityType = entityType;
            Columns = columns;
        }

        public string Table { get; }

        public Type EntityType { get; }

        public IReadOnlyList<WhereColumn> Columns { get; }

        public WhereColumn? Find(string name) =>
            Columns.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));

        public static WhereSchema For<TEntity>(GeoBenchContext context) => For(context, typeof(TEntity));

        /// <summary>Schemat tabeli o podanej nazwie w bazie (np. dzialka).</summary>
        public static WhereSchema ForTable(GeoBenchContext context, string table)
        {
            var entity = context.Model.GetEntityTypes().FirstOrDefault(e => e.GetTableName() == table)
                ?? throw new ArgumentException($"W modelu nie ma tabeli {table}.", nameof(table));
            return For(context, entity.ClrType);
        }

        public static WhereSchema For(GeoBenchContext context, Type entityType)
        {
            var entity = context.Model.FindEntityType(entityType)
                ?? throw new InvalidOperationException($"Typ {entityType.Name} nie należy do modelu.");
            var table = entity.GetTableName()!;
            var storeObject = StoreObjectIdentifier.Table(table, entity.GetSchema());
            var declared = entityType.GetProperties()
                .Select((p, index) => (p.Name, index))
                .ToDictionary(x => x.Name, x => x.index);

            var columns = new List<WhereColumn>();
            foreach (var property in entity.GetProperties()
                         .Where(p => p.PropertyInfo != null)
                         .OrderBy(p => p.IsPrimaryKey() ? -1 : declared.GetValueOrDefault(p.Name, int.MaxValue)))
            {
                var valueType = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
                if (Classify(valueType) is not { } kind)
                    continue;
                var storeType = property.GetColumnType(storeObject) ?? "";
                var isUtc = storeType.Contains("with time zone", StringComparison.OrdinalIgnoreCase);
                columns.Add(new WhereColumn(property.GetColumnName(storeObject) ?? property.Name, kind, property.PropertyInfo!,
                    property.IsNullable, isUtc));
            }
            return new WhereSchema(table, entityType, columns);
        }

        private static WhereColumnKind? Classify(Type type)
        {
            if (type == typeof(string))
                return WhereColumnKind.Text;
            if (type.IsEnum)
                return WhereColumnKind.Enum;
            if (type == typeof(int) || type == typeof(long) || type == typeof(short))
                return WhereColumnKind.Integer;
            if (type == typeof(decimal) || type == typeof(double) || type == typeof(float))
                return WhereColumnKind.Decimal;
            if (type == typeof(DateOnly))
                return WhereColumnKind.Date;
            if (type == typeof(DateTime))
                return WhereColumnKind.DateTime;
            return null;
        }
    }
}
