using NetTopologySuite.Geometries;

namespace GeoBench.Core.Model
{
    /// <summary>
    /// Budynek z usługi WFS EGiB. Nazwy pól odpowiadają polom usługi.
    /// </summary>
    public class Budynek
    {
        public int Oid { get; set; }

        /// <summary>Identyfikator budynku z sufiksem _BUD (np. 246401_1.0424.27_BUD).</summary>
        public required string IdBudynku { get; set; }

        /// <summary>Rodzaj budynku według KŚT (np. m).</summary>
        public string? Rodzaj { get; set; }

        public int? KondygnacjeNadziemne { get; set; }
        public int? KondygnacjePodziemne { get; set; }

        /// <summary>Data z usługi (czas lokalny bez strefy; w praktyce data wygenerowania danych).</summary>
        public DateTime? Data { get; set; }

        /// <summary>Geometria w EPSG:2180; null, gdy geometrii nie dało się naprawić.</summary>
        public MultiPolygon? Geom { get; set; }
    }
}
