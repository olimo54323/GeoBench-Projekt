using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace GeoBench.Core.Data.Bulk
{
    /// <summary>Kolumna tabeli do zapisu masowego: nazwa i typ w bazie oraz odczyt wartości z encji.</summary>
    public sealed class BulkColumn
    {
        private readonly Func<object, object?> _getValue;

        internal BulkColumn(string name, string storeType, Type clrType, Func<object, object?> getValue)
        {
            Name = name;
            StoreType = storeType;
            ClrType = clrType;
            _getValue = getValue;
        }

        public string Name { get; }

        /// <summary>Typ kolumny w bazie (np. character varying(250), geometry(MultiPolygon,2180)).</summary>
        public string StoreType { get; }

        /// <summary>Typ wartości przekazywanej do bazy (po konwersji, bez Nullable).</summary>
        public Type ClrType { get; }

        public object? GetValue(object entity) => _getValue(entity);
    }

    /// <summary>
    /// Opis tabeli do SqlBulkCopy / COPY zbudowany z modelu EF: te same nazwy (snake_case), typy i konwersje
    /// co w migracjach, więc jedno mapowanie obsługuje wszystkie encje i oba silniki.
    /// Klucz generowany przez bazę (Oid) jest pomijany.
    /// </summary>
    public sealed class BulkTable<TEntity> where TEntity : class
    {
        private BulkTable(string name, IReadOnlyList<BulkColumn> columns)
        {
            Name = name;
            Columns = columns;
        }

        public string Name { get; }

        public IReadOnlyList<BulkColumn> Columns { get; }

        public static BulkTable<TEntity> FromModel(GeoBenchContext context)
        {
            var entity = context.Model.FindEntityType(typeof(TEntity))
                ?? throw new InvalidOperationException($"Typ {typeof(TEntity).Name} nie należy do modelu.");
            var table = entity.GetTableName()!;
            var storeObject = StoreObjectIdentifier.Table(table, entity.GetSchema());

            var columns = entity.GetProperties()
                .Where(p => !(p.IsPrimaryKey() && p.ValueGenerated != ValueGenerated.Never))
                .Select(p => CreateColumn(p, storeObject))
                .ToList();
            return new BulkTable<TEntity>(table, columns);
        }

        private static BulkColumn CreateColumn(IProperty property, StoreObjectIdentifier storeObject)
        {
            var info = property.PropertyInfo
                ?? throw new NotSupportedException($"Właściwość {property.Name} nie ma odpowiednika w klasie (cień); zapis masowy jej nie obsługuje.");
            var mapping = property.GetRelationalTypeMapping();
            var converter = mapping.Converter;
            var providerType = converter?.ProviderClrType ?? property.ClrType;

            return new BulkColumn(
                property.GetColumnName(storeObject)!,
                mapping.StoreType,
                Nullable.GetUnderlyingType(providerType) ?? providerType,
                entity =>
                {
                    var value = info.GetValue(entity);
                    return value == null || converter == null ? value : converter.ConvertToProvider(value);
                });
        }
    }
}
