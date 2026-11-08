using System.Globalization;
using System.Xml.Linq;
using GeoBench.Core.Spatial;
using NetTopologySuite;
using NetTopologySuite.Geometries;

namespace GeoBench.Core.Import.Gml
{
    /// <summary>
    /// Zamiana geometrii powierzchniowej GML na geometrię NetTopologySuite. Współrzędne zostają
    /// w kolejności z pliku i bez układu (SRID 0); osie i transformację obsługuje CoordinateTransformer.
    /// Obsługuje Polygon, Surface (PolygonPatch), MultiSurface, CompositeSurface i MultiPolygon (GML 2),
    /// pierścienie LinearRing oraz Ring z segmentami LineStringSegment, Arc, ArcString i Circle.
    /// </summary>
    public sealed class GmlGeometryParser
    {
        private static readonly HashSet<string> SurfaceTypes =
            ["Polygon", "Surface", "MultiSurface", "CompositeSurface", "MultiPolygon"];

        // Typy rozpoznawane jako geometria, żeby nie trafiły do atrybutów; Parse zgłosi dla nich NotSupportedException
        private static readonly HashSet<string> OtherGeometryTypes =
            ["Point", "MultiPoint", "LineString", "MultiLineString", "Curve", "MultiCurve", "CompositeCurve", "MultiGeometry"];

        private readonly GeometryFactory _factory = NtsGeometryServices.Instance.CreateGeometryFactory();

        public static bool IsGmlNamespace(string namespaceUri) =>
            namespaceUri.StartsWith("http://www.opengis.net/gml", StringComparison.Ordinal);

        public static bool IsGeometryElement(XElement element) =>
            IsGmlNamespace(element.Name.NamespaceName)
            && (SurfaceTypes.Contains(element.Name.LocalName) || OtherGeometryTypes.Contains(element.Name.LocalName));

        public SourceGeometry Parse(XElement element)
        {
            var state = new ParseState();
            var geometry = ParseSurface(element, state);
            var srsName = element.DescendantsAndSelf()
                .Select(e => e.Attribute("srsName")?.Value)
                .FirstOrDefault(v => v != null);
            return new SourceGeometry(geometry, srsName, state.ArcCount);
        }

        private Geometry ParseSurface(XElement element, ParseState state) => element.Name.LocalName switch
        {
            "Polygon" or "PolygonPatch" => ParsePolygon(element, state),
            "Surface" => ToPolygonOrMulti(GmlChildren(element, "patches").SelectMany(p => p.Elements())
                .Select(patch => ParsePolygon(patch, state))),
            "MultiSurface" or "CompositeSurface" => ToMultiPolygon(Members(element, "surfaceMember", "surfaceMembers")
                .Select(member => ParseSurface(member, state))),
            "MultiPolygon" => ToMultiPolygon(Members(element, "polygonMember", null)
                .Select(member => ParseSurface(member, state))),
            _ => throw new NotSupportedException($"Nieobsługiwany typ geometrii GML: {element.Name.LocalName}.")
        };

        private Polygon ParsePolygon(XElement element, ParseState state)
        {
            var exterior = GmlChildren(element, "exterior").Concat(GmlChildren(element, "outerBoundaryIs")).FirstOrDefault()
                ?? throw new FormatException("Poligon bez granicy zewnętrznej.");
            var shell = ParseRing(FirstChild(exterior), state);
            var holes = GmlChildren(element, "interior").Concat(GmlChildren(element, "innerBoundaryIs"))
                .Select(interior => ParseRing(FirstChild(interior), state))
                .ToArray();
            return _factory.CreatePolygon(shell, holes);
        }

        private LinearRing ParseRing(XElement ring, ParseState state)
        {
            var coordinates = new List<Coordinate>();
            switch (ring.Name.LocalName)
            {
                case "LinearRing":
                    coordinates.AddRange(ReadCoordinates(ring));
                    break;
                case "Ring":
                    foreach (var member in GmlChildren(ring, "curveMember"))
                        AppendCurve(FirstChild(member), coordinates, state);
                    break;
                default:
                    throw new NotSupportedException($"Nieobsługiwany typ pierścienia GML: {ring.Name.LocalName}.");
            }

            if (coordinates.Count > 0 && !coordinates[0].Equals2D(coordinates[^1]))
                coordinates.Add(coordinates[0].Copy());
            if (coordinates.Count < 4)
                throw new FormatException($"Pierścień ma za mało punktów ({coordinates.Count}).");
            return _factory.CreateLinearRing(coordinates.ToArray());
        }

        private static void AppendCurve(XElement curve, List<Coordinate> target, ParseState state)
        {
            switch (curve.Name.LocalName)
            {
                case "Curve":
                    foreach (var segment in GmlChildren(curve, "segments").SelectMany(s => s.Elements()))
                        AppendSegment(segment, target, state);
                    break;
                case "LineString":
                case "LinearRing":
                    AppendPoints(target, ReadCoordinates(curve));
                    break;
                case "CompositeCurve":
                    foreach (var member in GmlChildren(curve, "curveMember"))
                        AppendCurve(FirstChild(member), target, state);
                    break;
                default:
                    throw new NotSupportedException($"Nieobsługiwany typ krzywej GML: {curve.Name.LocalName}.");
            }
        }

        private static void AppendSegment(XElement segment, List<Coordinate> target, ParseState state)
        {
            var points = ReadCoordinates(segment);
            switch (segment.Name.LocalName)
            {
                case "LineStringSegment":
                    AppendPoints(target, points);
                    break;
                case "Arc":
                case "ArcString":
                    if (points.Length < 3 || points.Length % 2 == 0)
                        throw new FormatException($"{segment.Name.LocalName} wymaga nieparzystej liczby punktów (co najmniej 3).");
                    for (var i = 0; i + 2 < points.Length; i += 2)
                    {
                        AppendPoints(target, ArcLinearizer.Linearize(points[i], points[i + 1], points[i + 2]));
                        state.ArcCount++;
                    }
                    break;
                case "Circle":
                    if (points.Length != 3)
                        throw new FormatException("Circle wymaga dokładnie 3 punktów.");
                    AppendPoints(target, ArcLinearizer.LinearizeCircle(points[0], points[1], points[2]));
                    state.ArcCount++;
                    break;
                default:
                    throw new NotSupportedException($"Nieobsługiwany segment krzywej GML: {segment.Name.LocalName}.");
            }
        }

        // Kolejne segmenty dzielą punkt styku, więc pomijamy jego powtórzenie
        private static void AppendPoints(List<Coordinate> target, IReadOnlyList<Coordinate> points)
        {
            var start = target.Count > 0 && points.Count > 0 && target[^1].Equals2D(points[0]) ? 1 : 0;
            for (var i = start; i < points.Count; i++)
                target.Add(points[i]);
        }

        private static Coordinate[] ReadCoordinates(XElement element)
        {
            var posList = GmlChildren(element, "posList").FirstOrDefault();
            if (posList != null)
                return ParsePositions(posList.Value, Dimension(posList));

            var positions = GmlChildren(element, "pos").ToList();
            if (positions.Count > 0)
                return positions.SelectMany(p => ParsePositions(p.Value, Dimension(p))).ToArray();

            var coordinates = GmlChildren(element, "coordinates").FirstOrDefault();
            if (coordinates != null)
                return ParseGml2Coordinates(coordinates.Value);

            throw new FormatException($"Brak współrzędnych w elemencie {element.Name.LocalName}.");
        }

        // srsDimension może stać na posList albo na dowolnym elemencie nadrzędnym geometrii
        private static int Dimension(XElement element)
        {
            var value = element.AncestorsAndSelf()
                .Select(e => e.Attribute("srsDimension")?.Value)
                .FirstOrDefault(v => v != null);
            var dimension = value == null ? 2 : int.Parse(value, CultureInfo.InvariantCulture);
            if (dimension < 2)
                throw new FormatException($"Nieprawidłowy wymiar współrzędnych: {dimension}.");
            return dimension;
        }

        // Przy współrzędnych 3D bierzemy tylko dwie pierwsze wartości
        private static Coordinate[] ParsePositions(string text, int dimension)
        {
            var values = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (values.Length % dimension != 0)
                throw new FormatException($"Liczba wartości ({values.Length}) nie jest wielokrotnością wymiaru {dimension}.");

            var result = new Coordinate[values.Length / dimension];
            for (var i = 0; i < result.Length; i++)
                result[i] = new Coordinate(ParseDouble(values[i * dimension]), ParseDouble(values[i * dimension + 1]));
            return result;
        }

        // GML 2: krotki rozdzielone spacją, wartości przecinkiem (x,y x,y)
        private static Coordinate[] ParseGml2Coordinates(string text) =>
            text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                .Select(tuple => tuple.Split(','))
                .Select(v => new Coordinate(ParseDouble(v[0]), ParseDouble(v[1])))
                .ToArray();

        private static double ParseDouble(string value) =>
            double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);

        private Geometry ToPolygonOrMulti(IEnumerable<Polygon> polygons)
        {
            var list = polygons.ToArray();
            return list.Length == 1 ? list[0] : _factory.CreateMultiPolygon(list);
        }

        private MultiPolygon ToMultiPolygon(IEnumerable<Geometry> parts) =>
            _factory.CreateMultiPolygon(parts
                .SelectMany(g => g is MultiPolygon mp ? mp.Geometries.Cast<Polygon>() : new[] { (Polygon)g })
                .ToArray());

        private static IEnumerable<XElement> GmlChildren(XElement element, string localName) =>
            element.Elements().Where(e => e.Name.LocalName == localName && IsGmlNamespace(e.Name.NamespaceName));

        // Elementy członkowskie: pojedyncze (surfaceMember) zawierają jedną geometrię, zbiorcze (surfaceMembers) wiele
        private static IEnumerable<XElement> Members(XElement element, string singleName, string? multipleName) =>
            element.Elements()
                .Where(e => IsGmlNamespace(e.Name.NamespaceName))
                .SelectMany(e => e.Name.LocalName == singleName ? e.Elements().Take(1)
                    : e.Name.LocalName == multipleName ? e.Elements()
                    : Enumerable.Empty<XElement>());

        private static XElement FirstChild(XElement element) =>
            element.Elements().FirstOrDefault()
            ?? throw new FormatException($"Element {element.Name.LocalName} nie zawiera geometrii.");

        private sealed class ParseState
        {
            public int ArcCount;
        }
    }
}
