using NetTopologySuite.Geometries;

namespace GeoBench.Core.Spatial
{
    /// <summary>
    /// Zamiana łuku okręgu (zadanego trzema punktami, jak gml:Arc) na odcinki. NetTopologySuite nie ma
    /// krzywych, więc łuki z GML trzeba wyprostować. Krok dobierany jest tak, żeby cięciwa odbiegała
    /// od łuku najwyżej o maxDeviation (w jednostkach układu, tu metrach).
    /// </summary>
    public static class ArcLinearizer
    {
        public const double DefaultMaxDeviation = 0.005;

        /// <summary>Punkty łuku od start przez middle do end, łącznie z końcami.</summary>
        public static IReadOnlyList<Coordinate> Linearize(Coordinate start, Coordinate middle, Coordinate end,
            double maxDeviation = DefaultMaxDeviation)
        {
            if (!TryGetCircle(start, middle, end, out var center, out var radius))
                return [start, middle, end]; // punkty współliniowe: łuk jest odcinkiem

            var startAngle = Angle(center, start);
            var sweep = NormalizePositive(Angle(center, end) - startAngle);
            var toMiddle = NormalizePositive(Angle(center, middle) - startAngle);
            if (toMiddle > sweep)
                sweep -= 2 * Math.PI; // środkowy punkt leży po drugiej stronie: łuk zgodnie z ruchem wskazówek

            var points = Points(center, radius, startAngle, sweep, maxDeviation);
            points[0] = start;
            points[^1] = end;
            return points;
        }

        /// <summary>Pełny okrąg przez trzy punkty, zaczynający i kończący się w pierwszym z nich.</summary>
        public static IReadOnlyList<Coordinate> LinearizeCircle(Coordinate first, Coordinate second, Coordinate third,
            double maxDeviation = DefaultMaxDeviation)
        {
            if (!TryGetCircle(first, second, third, out var center, out var radius))
                throw new FormatException("Punkty okręgu są współliniowe.");

            var startAngle = Angle(center, first);
            var direction = NormalizePositive(Angle(center, second) - startAngle) < Math.PI ? 1 : -1;
            var points = Points(center, radius, startAngle, direction * 2 * Math.PI, maxDeviation);
            points[0] = first;
            points[^1] = first.Copy();
            return points;
        }

        private static Coordinate[] Points(Coordinate center, double radius, double startAngle, double sweep, double maxDeviation)
        {
            // Strzałka cięciwy dla kąta s: r(1 - cos(s/2)) <= maxDeviation
            var step = maxDeviation >= radius ? Math.PI / 2 : 2 * Math.Acos(1 - maxDeviation / radius);
            var count = Math.Max(2, (int)Math.Ceiling(Math.Abs(sweep) / step));

            var points = new Coordinate[count + 1];
            for (var i = 0; i <= count; i++)
            {
                var angle = startAngle + sweep * i / count;
                points[i] = new Coordinate(center.X + radius * Math.Cos(angle), center.Y + radius * Math.Sin(angle));
            }
            return points;
        }

        // Okrąg przez trzy punkty; obliczenia względem pierwszego punktu, bo duże współrzędne
        // (miliony metrów w PL-2000) podniesione do kwadratu tracą precyzję
        private static bool TryGetCircle(Coordinate a, Coordinate b, Coordinate c, out Coordinate center, out double radius)
        {
            double bx = b.X - a.X, by = b.Y - a.Y, cx = c.X - a.X, cy = c.Y - a.Y;
            var d = 2 * (bx * cy - by * cx);
            if (Math.Abs(d) < 1e-12)
            {
                center = new Coordinate();
                radius = 0;
                return false;
            }

            var b2 = bx * bx + by * by;
            var c2 = cx * cx + cy * cy;
            var ux = (cy * b2 - by * c2) / d;
            var uy = (bx * c2 - cx * b2) / d;
            center = new Coordinate(a.X + ux, a.Y + uy);
            radius = Math.Sqrt(ux * ux + uy * uy);
            return true;
        }

        private static double Angle(Coordinate center, Coordinate point) =>
            Math.Atan2(point.Y - center.Y, point.X - center.X);

        private static double NormalizePositive(double angle)
        {
            var result = angle % (2 * Math.PI);
            return result < 0 ? result + 2 * Math.PI : result;
        }
    }
}
