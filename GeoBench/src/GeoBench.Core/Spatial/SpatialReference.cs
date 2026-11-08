using System.Globalization;

namespace GeoBench.Core.Spatial
{
    /// <summary>
    /// Układ współrzędnych odczytany z atrybutu srsName i kolejność osi współrzędnych w pliku.
    /// Formy URN i http://www.opengis.net/def/crs/ oznaczają kolejność osi według definicji EPSG,
    /// czyli dla PL-1992, PL-2000 i WGS 84 północ (szerokość) pierwsza. Starsze formy EPSG:xxxx
    /// i epsg.xml#xxxx oznaczają wschód (długość) pierwszy.
    /// </summary>
    public sealed record SpatialReference(int Epsg, bool NorthingFirst)
    {
        // Układy, których definicja EPSG ma oś północną (szerokość) jako pierwszą
        private static readonly HashSet<int> NorthingFirstCodes = [2176, 2177, 2178, 2179, 2180, 4326];

        public static SpatialReference Parse(string srsName)
        {
            var value = srsName.Trim();
            bool authorityAxisOrder;
            string code;

            if (value.StartsWith("urn:ogc:def:crs:EPSG:", StringComparison.OrdinalIgnoreCase))
            {
                // urn:ogc:def:crs:EPSG::2180 albo z wersją: urn:ogc:def:crs:EPSG:6.9:2180
                authorityAxisOrder = true;
                code = value[(value.LastIndexOf(':') + 1)..];
            }
            else if (value.StartsWith("http://www.opengis.net/def/crs/EPSG/", StringComparison.OrdinalIgnoreCase)
                     || value.StartsWith("https://www.opengis.net/def/crs/EPSG/", StringComparison.OrdinalIgnoreCase))
            {
                authorityAxisOrder = true;
                code = value[(value.LastIndexOf('/') + 1)..];
            }
            else if (value.StartsWith("EPSG:", StringComparison.OrdinalIgnoreCase))
            {
                authorityAxisOrder = false;
                code = value[5..];
            }
            else if (value.Contains("epsg.xml#", StringComparison.OrdinalIgnoreCase))
            {
                authorityAxisOrder = false;
                code = value[(value.LastIndexOf('#') + 1)..];
            }
            else
            {
                throw new FormatException($"Nierozpoznany zapis układu współrzędnych: {srsName}.");
            }

            if (!int.TryParse(code, NumberStyles.None, CultureInfo.InvariantCulture, out var epsg))
                throw new FormatException($"Nieprawidłowy kod EPSG w {srsName}.");

            return new SpatialReference(epsg, authorityAxisOrder && NorthingFirstCodes.Contains(epsg));
        }
    }
}
