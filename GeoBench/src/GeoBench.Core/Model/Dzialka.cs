using NetTopologySuite.Geometries;

namespace GeoBench.Core.Model
{
    /// <summary>
    /// Działka ewidencyjna z usługi WFS EGiB. Nazwy pól odpowiadają polom usługi.
    /// </summary>
    public class Dzialka
    {
        public int Oid { get; set; }

        /// <summary>Identyfikator działki: TERYT jednostki, obręb, numer (np. 246401_1.0219.37/6).</summary>
        public required string IdDzialki { get; set; }

        public string? NumerDzialki { get; set; }
        public string? NumerObrebu { get; set; }
        public string? NazwaObrebu { get; set; }

        /// <summary>Identyfikator jednostki ewidencyjnej (TERYT, np. 246401_1).</summary>
        public string? NumerJednostki { get; set; }

        public string? NazwaGminy { get; set; }

        /// <summary>Pole ewidencyjne w hektarach.</summary>
        public decimal? PoleEwidencyjne { get; set; }

        public string? KlasouzytkiEgib { get; set; }
        public string? GrupaRejestrowa { get; set; }

        /// <summary>Data z usługi (czas lokalny bez strefy; w praktyce data wygenerowania danych).</summary>
        public DateTime? Data { get; set; }

        /// <summary>Geometria w EPSG:2180; null, gdy geometrii nie dało się naprawić.</summary>
        public MultiPolygon? Geom { get; set; }
    }
}
