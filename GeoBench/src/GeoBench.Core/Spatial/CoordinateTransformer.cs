using System.Collections.Concurrent;
using NetTopologySuite;
using NetTopologySuite.Geometries;
using ProjNet.CoordinateSystems;
using ProjNet.CoordinateSystems.Transformations;

namespace GeoBench.Core.Spatial
{
    /// <summary>
    /// Zamiana kolejności osi i transformacja geometrii między układami (ProjNet). Wynik ma zawsze
    /// kolejność wschód, północ (X, Y), jak wymaga NetTopologySuite, i SRID układu docelowego.
    /// </summary>
    public static class CoordinateTransformer
    {
        private static readonly ConcurrentDictionary<(int From, int To), MathTransform> Transforms = new();

        public static Geometry Transform(Geometry geometry, SpatialReference source, int targetEpsg)
        {
            var transform = source.Epsg == targetEpsg ? null : Transforms.GetOrAdd((source.Epsg, targetEpsg), CreateTransform);

            // CreateGeometry kopiuje współrzędne, więc oryginał zostaje nietknięty
            var result = NtsGeometryServices.Instance.CreateGeometryFactory(targetEpsg).CreateGeometry(geometry);
            result.Apply(new AxisAndProjectionFilter(source.NorthingFirst, transform));
            return result;
        }

        private static MathTransform CreateTransform((int From, int To) key) =>
            new CoordinateTransformationFactory()
                .CreateFromCoordinateSystems(CoordinateSystems.Get(key.From), CoordinateSystems.Get(key.To))
                .MathTransform;

        private sealed class AxisAndProjectionFilter(bool swapAxes, MathTransform? transform) : ICoordinateSequenceFilter
        {
            public bool Done => false;

            public bool GeometryChanged => true;

            public void Filter(CoordinateSequence sequence, int index)
            {
                double x = sequence.GetX(index), y = sequence.GetY(index);
                if (swapAxes)
                    (x, y) = (y, x);
                if (transform != null)
                    (x, y) = transform.Transform(x, y);
                sequence.SetOrdinate(index, Ordinate.X, x);
                sequence.SetOrdinate(index, Ordinate.Y, y);
            }
        }
    }

    /// <summary>
    /// Definicje układów używanych w projekcie (ProjNet nie zawiera bazy EPSG).
    /// PL-2000 i PL-1992 mają tę samą elipsoidę GRS 80 i zerowe przesunięcie datum, więc przeliczenie
    /// między nimi jest czystą zmianą odwzorowania, jak w praktyce geodezyjnej.
    /// </summary>
    public static class CoordinateSystems
    {
        private static readonly ConcurrentDictionary<int, CoordinateSystem> Cache = new();

        public static CoordinateSystem Get(int epsg) => Cache.GetOrAdd(epsg, Create);

        private static CoordinateSystem Create(int epsg) => epsg switch
        {
            4326 => GeographicCoordinateSystem.WGS84,
            3857 => ProjectedCoordinateSystem.WebMercator,
            2176 => TransverseMercator(epsg, "ETRF2000-PL / CS2000/15", 15, 0.999923, 5500000, 0),
            2177 => TransverseMercator(epsg, "ETRF2000-PL / CS2000/18", 18, 0.999923, 6500000, 0),
            2178 => TransverseMercator(epsg, "ETRF2000-PL / CS2000/21", 21, 0.999923, 7500000, 0),
            2179 => TransverseMercator(epsg, "ETRF2000-PL / CS2000/24", 24, 0.999923, 8500000, 0),
            2180 => TransverseMercator(epsg, "ETRF2000-PL / CS92", 19, 0.9993, 500000, -5300000),
            _ => throw new NotSupportedException($"Układ EPSG:{epsg} nie jest obsługiwany.")
        };

        private static CoordinateSystem TransverseMercator(int epsg, string name, double centralMeridian, double scaleFactor,
            double falseEasting, double falseNorthing)
        {
            var wkt = FormattableString.Invariant(
                $"PROJCS[\"{name}\",GEOGCS[\"ETRS89\",DATUM[\"European_Terrestrial_Reference_System_1989\",SPHEROID[\"GRS 1980\",6378137,298.257222101],TOWGS84[0,0,0,0,0,0,0]],PRIMEM[\"Greenwich\",0],UNIT[\"degree\",0.0174532925199433]],PROJECTION[\"Transverse_Mercator\"],PARAMETER[\"latitude_of_origin\",0],PARAMETER[\"central_meridian\",{centralMeridian}],PARAMETER[\"scale_factor\",{scaleFactor}],PARAMETER[\"false_easting\",{falseEasting}],PARAMETER[\"false_northing\",{falseNorthing}],UNIT[\"metre\",1],AUTHORITY[\"EPSG\",\"{epsg}\"]]");
            return (CoordinateSystem)new CoordinateSystemFactory().CreateFromWkt(wkt);
        }
    }
}
