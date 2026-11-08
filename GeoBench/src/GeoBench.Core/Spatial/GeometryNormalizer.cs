using CommunityToolkit.Diagnostics;
using GeoBench.Core.Data;
using NetTopologySuite;
using NetTopologySuite.Geometries;
using NetTopologySuite.Geometries.Utilities;
using NetTopologySuite.Operation.Valid;

namespace GeoBench.Core.Spatial
{
    public enum GeometryStatus
    {
        Valid,
        Repaired,
        Rejected
    }

    /// <summary>Wynik normalizacji: geometria do zapisu (null przy odrzuceniu), status i powód.</summary>
    public sealed record NormalizedGeometry(MultiPolygon? Geometry, GeometryStatus Status, string? Reason);

    /// <summary>
    /// Doprowadza geometrię w EPSG:2180 do postaci zapisywanej w bazie: MultiPolygon, poprawny według OGC.
    /// Niepoprawną geometrię naprawia GeometryFixer; z wyniku zostają tylko poligony.
    /// </summary>
    public static class GeometryNormalizer
    {
        private static readonly GeometryFactory Factory = NtsGeometryServices.Instance.CreateGeometryFactory(GeoBenchContext.Srid);

        public static NormalizedGeometry Normalize(Geometry? geometry)
        {
            if (geometry is null || geometry.IsEmpty)
                return new NormalizedGeometry(null, GeometryStatus.Rejected, "Brak geometrii.");
            // Geometria musi być już przeliczona do układu bazy (CoordinateTransformer)
            Guard.IsEqualTo(geometry.SRID, GeoBenchContext.Srid, nameof(geometry));

            var status = GeometryStatus.Valid;
            string? reason = null;
            if (!geometry.IsValid)
            {
                reason = new IsValidOp(geometry).ValidationError?.ToString();
                geometry = GeometryFixer.Fix(geometry);
                status = GeometryStatus.Repaired;
            }

            var result = ToMultiPolygon(geometry);
            if (result != null && !result.IsValid)
            {
                // Poligony wydobyte z kolekcji mogą na siebie nachodzić; ponowna naprawa je scala
                result = ToMultiPolygon(GeometryFixer.Fix(result));
                status = GeometryStatus.Repaired;
            }

            if (result == null || !result.IsValid)
                return new NormalizedGeometry(null, GeometryStatus.Rejected, reason ?? "Geometria nie zawiera poprawnych poligonów.");
            return new NormalizedGeometry(result, status, reason);
        }

        private static MultiPolygon? ToMultiPolygon(Geometry geometry)
        {
            var polygons = PolygonExtracter.GetPolygons(geometry)
                .Cast<Polygon>()
                .Where(p => !p.IsEmpty)
                .Select(p => (Polygon)Factory.CreateGeometry(p))
                .ToArray();
            return polygons.Length == 0 ? null : Factory.CreateMultiPolygon(polygons);
        }
    }
}
