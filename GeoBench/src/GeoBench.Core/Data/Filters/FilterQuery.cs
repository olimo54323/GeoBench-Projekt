using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;

namespace GeoBench.Core.Data.Filters
{
    /// <summary>Zapytania pomocnicze buildera filtrów: liczba pasujących wierszy i najczęstsze wartości kolumny.</summary>
    public static class FilterQuery
    {
        private static readonly MethodInfo CountMethod = typeof(FilterQuery).GetMethod(nameof(CountCoreAsync), BindingFlags.NonPublic | BindingFlags.Static)!;
        private static readonly MethodInfo TopValuesMethod = typeof(FilterQuery).GetMethod(nameof(TopValuesCoreAsync), BindingFlags.NonPublic | BindingFlags.Static)!;

        /// <summary>Liczba wierszy spełniających warunek i liczba wszystkich wierszy tabeli.</summary>
        public static Task<(int Matching, int Total)> CountAsync(GeoBenchContext context, WhereSchema schema, string? where,
            CancellationToken cancellationToken = default) =>
            (Task<(int, int)>)CountMethod.MakeGenericMethod(schema.EntityType).Invoke(null, [context, schema, where, cancellationToken])!;

        /// <summary>Najczęstsze wartości kolumny (bez NULL), sformatowane jak w oknie informacji.</summary>
        public static Task<IReadOnlyList<string>> TopValuesAsync(GeoBenchContext context, WhereSchema schema, WhereColumn column, int limit,
            CancellationToken cancellationToken = default) =>
            (Task<IReadOnlyList<string>>)TopValuesMethod.MakeGenericMethod(schema.EntityType, column.Property.PropertyType)
                .Invoke(null, [context, column, limit, cancellationToken])!;

        private static async Task<(int, int)> CountCoreAsync<TEntity>(GeoBenchContext context, WhereSchema schema, string? where,
            CancellationToken cancellationToken) where TEntity : class
        {
            var filter = WhereFilter.Compile<TEntity>(where, schema);
            var set = context.Set<TEntity>().AsNoTracking();
            var total = await set.CountAsync(cancellationToken);
            var matching = filter == null ? total : await set.Where(filter).CountAsync(cancellationToken);
            return (matching, total);
        }

        private static async Task<IReadOnlyList<string>> TopValuesCoreAsync<TEntity, TKey>(GeoBenchContext context, WhereColumn column, int limit,
            CancellationToken cancellationToken) where TEntity : class
        {
            var parameter = Expression.Parameter(typeof(TEntity), "x");
            var member = Expression.Property(parameter, column.Property);
            var key = Expression.Lambda<Func<TEntity, TKey>>(member, parameter);

            IQueryable<TEntity> query = context.Set<TEntity>().AsNoTracking();
            if (column.IsNullable)
                query = query.Where(Expression.Lambda<Func<TEntity, bool>>(Expression.NotEqual(member, Expression.Constant(null, typeof(TKey))), parameter));

            var rows = await query
                .GroupBy(key)
                .Select(g => new { g.Key, Count = g.Count() })
                .OrderByDescending(g => g.Count)
                .Take(limit)
                .ToListAsync(cancellationToken);
            return rows.Select(r => FeatureAttributes.Format(r.Key)).ToList();
        }
    }
}
