using GeoBench.Core.Data;
using NetTopologySuite;
using NetTopologySuite.Densify;
using NetTopologySuite.Geometries;

namespace GeoBench.Core.Spatial
{
    /// <summary>
    /// Przeliczenia dla podglądu na mapie (Web Mercator, EPSG:3857, jak kafelki OSM): geometria z bazy do rysowania
    /// i okno widoku do zapytania. Prostokąt w jednym układzie jest w drugim lekko wygiętym czworobokiem,
    /// więc okno jest zagęszczane przed przeliczeniem, a wynikiem jest jego obwiednia.
    /// </summary>
    public static class MapProjection
    {
        public const int WebMercator = 3857;

        private static readonly SpatialReference Pl1992 = new(GeoBenchContext.Srid, NorthingFirst: false);
        private static readonly SpatialReference Mercator = new(WebMercator, NorthingFirst: false);

        public static Geometry ToWebMercator(Geometry geometry) => CoordinateTransformer.Transform(geometry, Pl1992, WebMercator);

        /// <summary>Okno widoku mapy (3857) jako obwiednia w układzie bazy (2180).</summary>
        public static Envelope ToPl1992(Envelope webMercator) => TransformEnvelope(webMercator, WebMercator, Mercator, GeoBenchContext.Srid);

        /// <summary>Obwiednia w układzie bazy (2180) jako prostokąt mapy (3857), np. zasięg aktu do przybliżenia.</summary>
        public static Envelope ToWebMercator(Envelope pl1992) => TransformEnvelope(pl1992, GeoBenchContext.Srid, Pl1992, WebMercator);

        private static Envelope TransformEnvelope(Envelope envelope, int sourceSrid, SpatialReference source, int targetSrid)
        {
            if (envelope.IsNull)
                return new Envelope();
            var factory = NtsGeometryServices.Instance.CreateGeometryFactory(sourceSrid);
            // 16 odcinków na bok wystarcza, żeby wygięcie boków nie obcięło okna
            var ring = Densifier.Densify(factory.ToGeometry(envelope), Math.Max(envelope.Width, envelope.Height) / 16);
            return CoordinateTransformer.Transform(ring, source, targetSrid).EnvelopeInternal;
        }
    }
}
