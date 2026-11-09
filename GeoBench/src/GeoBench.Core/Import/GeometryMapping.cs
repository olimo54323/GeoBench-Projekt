using GeoBench.Core.Data;
using GeoBench.Core.Spatial;
using NetTopologySuite.Geometries;

namespace GeoBench.Core.Import
{
    /// <summary>Geometria gotowa do zapisu: MultiPolygon w układzie bazy albo null z powodem.</summary>
    public readonly record struct MappedGeometry(MultiPolygon? Geometry, GeometryStatus Status, string? Reason, int Arcs);

    /// <summary>
    /// Wspólna droga geometrii ze źródła do bazy: układ z srsName (z kolejnością osi), transformacja do EPSG:2180,
    /// normalizacja do poprawnego MultiPolygonu. Błąd układu lub geometrii nie odrzuca obiektu, tylko jego geometrię.
    /// </summary>
    public static class GeometryMapping
    {
        public static MappedGeometry Map(FeatureRecord record, string property)
        {
            var source = record.GetGeometry(property);
            if (source == null)
            {
                var missing = GeometryNormalizer.Normalize(null);
                return new MappedGeometry(null, missing.Status, missing.Reason, 0);
            }

            try
            {
                var reference = SpatialReference.Parse(source.SrsName ?? throw new FormatException("Geometria nie ma atrybutu srsName."));
                var normalized = GeometryNormalizer.Normalize(CoordinateTransformer.Transform(source.Geometry, reference, GeoBenchContext.Srid));
                return new MappedGeometry(normalized.Geometry, normalized.Status, normalized.Reason, source.ArcCount);
            }
            catch (Exception ex) when (ex is FormatException or NotSupportedException)
            {
                return new MappedGeometry(null, GeometryStatus.Rejected, ex.Message, source.ArcCount);
            }
        }
    }
}
