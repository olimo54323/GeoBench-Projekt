using System.Linq.Expressions;
using GeoBench.Core.Model;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite;
using NetTopologySuite.Geometries;

namespace GeoBench.Core.Data
{
    /// <summary>Obiekty jednej tabeli znalezione w punkcie mapy; Truncated, gdy było ich więcej niż limit.</summary>
    public sealed record IdentifyResult(string Table, IReadOnlyList<FeatureRecord> Records, bool Truncated);

    /// <summary>
    /// Identyfikacja obiektów w klikniętym miejscu mapy: pełne rekordy przecinające mały kwadrat wokół punktu
    /// (tolerancja kilku pikseli w EPSG:2180). Ten sam LINQ w obu bazach; podgląd nie jest pomiarem.
    /// </summary>
    public static class MapIdentifyQuery
    {
        /// <summary>Najwięcej obiektów jednej tabeli w oknie informacji.</summary>
        public const int MaxPerTable = 50;

        private static readonly GeometryFactory Factory = NtsGeometryServices.Instance.CreateGeometryFactory(GeoBenchContext.Srid);

        public static async Task<IdentifyResult> StrefyAsync(GeoBenchContext context, Envelope area, CancellationToken cancellationToken,
            Expression<Func<StrefaPlanistyczna, bool>>? filter = null)
        {
            var obszar = Factory.ToGeometry(area);
            var rows = await MapDataQuery.Filtered(context.StrefyPlanistyczne, filter)
                .Where(s => s.Geom != null && s.Geom.Intersects(obszar))
                .OrderBy(s => s.Oid)
                .Take(MaxPerTable + 1)
                .ToListAsync(cancellationToken);
            return Result(context, rows, s => s.Oznaczenie);
        }

        public static async Task<IdentifyResult> DzialkiAsync(GeoBenchContext context, Envelope area, CancellationToken cancellationToken,
            Expression<Func<Dzialka, bool>>? filter = null)
        {
            var obszar = Factory.ToGeometry(area);
            var rows = await MapDataQuery.Filtered(context.Dzialki, filter)
                .Where(d => d.Geom != null && d.Geom.Intersects(obszar))
                .OrderBy(d => d.Oid)
                .Take(MaxPerTable + 1)
                .ToListAsync(cancellationToken);
            return Result(context, rows, d => d.IdDzialki);
        }

        public static async Task<IdentifyResult> BudynkiAsync(GeoBenchContext context, Envelope area, CancellationToken cancellationToken,
            Expression<Func<Budynek, bool>>? filter = null)
        {
            var obszar = Factory.ToGeometry(area);
            var rows = await MapDataQuery.Filtered(context.Budynki, filter)
                .Where(b => b.Geom != null && b.Geom.Intersects(obszar))
                .OrderBy(b => b.Oid)
                .Take(MaxPerTable + 1)
                .ToListAsync(cancellationToken);
            return Result(context, rows, b => b.IdBudynku);
        }

        private static IdentifyResult Result<TEntity>(GeoBenchContext context, List<TEntity> rows, Func<TEntity, string> id)
            where TEntity : class
        {
            var truncated = rows.Count > MaxPerTable;
            if (truncated)
                rows.RemoveAt(rows.Count - 1);
            var records = rows.Select(r => FeatureAttributes.FromEntity(context, r, id(r))).ToList();
            return new IdentifyResult(FeatureAttributes.TableName<TEntity>(context), records, truncated);
        }
    }
}
