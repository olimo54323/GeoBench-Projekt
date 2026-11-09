using System.Linq.Expressions;
using GeoBench.Core.Model;
using GeoBench.Core.Spatial;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite;
using NetTopologySuite.Geometries;

namespace GeoBench.Core.Data
{
    /// <summary>Obiekt do narysowania: identyfikator, etykieta, kategoria (symbol strefy, rodzaj budynku) i geometria w 3857.</summary>
    public sealed record MapFeature(string Id, string? Label, string? Category, Geometry Geometry);

    /// <summary>Obiekty z okna; Truncated, gdy w oknie jest ich więcej niż limit.</summary>
    public sealed record MapQueryResult(IReadOnlyList<MapFeature> Features, bool Truncated)
    {
        public static readonly MapQueryResult Empty = new([], false);
    }

    /// <summary>
    /// Zapytania okienkowe dla podglądu mapy (Intersects z oknem widoku, bez śledzenia EF), te same w obu bazach.
    /// Podgląd nie jest pomiarem. Geometria jest przeliczana do 3857 po stronie aplikacji.
    /// </summary>
    public static class MapDataQuery
    {
        /// <summary>Przybliżony zasięg Częstochowy w PL-1992, gdy w bazie nie ma jeszcze aktu planowania.</summary>
        public static readonly Envelope DefaultExtent = new(495000, 525000, 315000, 340000);

        private static readonly GeometryFactory Factory = NtsGeometryServices.Instance.CreateGeometryFactory(GeoBenchContext.Srid);

        /// <summary>Zasięg do przybliżenia na starcie (zasięg aktu planowania); null, gdy w bazie nie ma aktu.</summary>
        public static async Task<Envelope?> StartExtentAsync(GeoBenchContext context, CancellationToken cancellationToken)
        {
            var zasieg = await context.AktyPlanowania.AsNoTracking()
                .Where(a => a.Zasieg != null)
                .Select(a => a.Zasieg)
                .FirstOrDefaultAsync(cancellationToken);
            return zasieg?.EnvelopeInternal;
        }

        public static async Task<MapQueryResult> StrefyAsync(GeoBenchContext context, Envelope window, int limit, CancellationToken cancellationToken,
            Expression<Func<StrefaPlanistyczna, bool>>? filter = null)
        {
            var okno = Factory.ToGeometry(window);
            var rows = await Filtered(context.StrefyPlanistyczne, filter)
                .Where(s => s.Geom != null && s.Geom.Intersects(okno))
                .Select(s => new { s.Oznaczenie, s.Symbol, s.Geom })
                .Take(limit + 1)
                .ToListAsync(cancellationToken);
            return Result(rows.Select(r => new MapFeature(r.Oznaczenie, r.Oznaczenie, r.Symbol, MapProjection.ToWebMercator(r.Geom!))), limit);
        }

        public static async Task<MapQueryResult> DzialkiAsync(GeoBenchContext context, Envelope window, int limit, CancellationToken cancellationToken,
            Expression<Func<Dzialka, bool>>? filter = null)
        {
            var okno = Factory.ToGeometry(window);
            var rows = await Filtered(context.Dzialki, filter)
                .Where(d => d.Geom != null && d.Geom.Intersects(okno))
                .Select(d => new { d.IdDzialki, d.NumerDzialki, d.Geom })
                .Take(limit + 1)
                .ToListAsync(cancellationToken);
            return Result(rows.Select(r => new MapFeature(r.IdDzialki, r.NumerDzialki, null, MapProjection.ToWebMercator(r.Geom!))), limit);
        }

        public static async Task<MapQueryResult> BudynkiAsync(GeoBenchContext context, Envelope window, int limit, CancellationToken cancellationToken,
            Expression<Func<Budynek, bool>>? filter = null)
        {
            var okno = Factory.ToGeometry(window);
            var rows = await Filtered(context.Budynki, filter)
                .Where(b => b.Geom != null && b.Geom.Intersects(okno))
                .Select(b => new { b.IdBudynku, b.Rodzaj, b.Geom })
                .Take(limit + 1)
                .ToListAsync(cancellationToken);
            return Result(rows.Select(r => new MapFeature(r.IdBudynku, null, r.Rodzaj, MapProjection.ToWebMercator(r.Geom!))), limit);
        }

        /// <summary>Symbole stref spełniających filtr (do legendy: pozostałe rodzaje są wyszarzone).</summary>
        public static async Task<IReadOnlyList<string>> ZoneSymbolsAsync(GeoBenchContext context,
            Expression<Func<StrefaPlanistyczna, bool>>? filter, CancellationToken cancellationToken)
        {
            return await Filtered(context.StrefyPlanistyczne, filter)
                .Select(s => s.Symbol)
                .Distinct()
                .ToListAsync(cancellationToken);
        }

        /// <summary>Tabela bez śledzenia EF, z filtrem warstwy, gdy jest aktywny.</summary>
        internal static IQueryable<TEntity> Filtered<TEntity>(DbSet<TEntity> set, Expression<Func<TEntity, bool>>? filter)
            where TEntity : class
        {
            var query = set.AsNoTracking();
            return filter == null ? query : query.Where(filter);
        }

        private static MapQueryResult Result(IEnumerable<MapFeature> features, int limit)
        {
            var list = features.ToList();
            var truncated = list.Count > limit;
            if (truncated)
                list.RemoveAt(list.Count - 1);
            return new MapQueryResult(list, truncated);
        }
    }
}
