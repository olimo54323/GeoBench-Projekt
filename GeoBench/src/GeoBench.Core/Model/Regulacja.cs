using NetTopologySuite.Geometries;

namespace GeoBench.Core.Model
{
    /// <summary>
    /// Regulacja planu ogólnego (schemat APP 2.0): obszar uzupełnienia zabudowy (OUZ) lub zabudowy śródmiejskiej (OZS).
    /// W odróżnieniu od stref regulacje są nakładkami: mogą nachodzić na wiele stref i na siebie nawzajem.
    /// </summary>
    public class Regulacja
    {
        public int Oid { get; set; }

        public int AktPlanowaniaOid { get; set; }
        public AktPlanowania AktPlanowania { get; set; } = null!;

        /// <summary>Podklasa regulacji (typ obiektu w GML).</summary>
        public RodzajRegulacji Rodzaj { get; set; }

        /// <summary>Oznaczenie regulacji, unikalne w obrębie aktu (np. 58OUZ).</summary>
        public required string Oznaczenie { get; set; }

        /// <summary>Identyfikator lokalny z idIIP (np. 1POG-58OUZ).</summary>
        public required string LokalnyId { get; set; }

        /// <summary>Symbol regulacji (OUZ, OZS).</summary>
        public required string Symbol { get; set; }

        /// <summary>Nazwa z pola "nazwa" (tekst, np. Obszar uzupełnienia zabudowy).</summary>
        public string? Nazwa { get; set; }

        /// <summary>Kod statusu z listy INSPIRE ProcessStepGeneralValue (np. elaboration).</summary>
        public string? Status { get; set; }

        /// <summary>Kod z listy INSPIRE RegulationNatureValue (np. generallyBinding).</summary>
        public string? CharakterUstalenia { get; set; }

        public DateOnly? ObowiazujeOd { get; set; }

        /// <summary>Początek wersji obiektu (UTC).</summary>
        public DateTime? PoczatekWersjiObiektu { get; set; }

        /// <summary>Geometria w EPSG:2180; null, gdy geometrii nie dało się naprawić.</summary>
        public MultiPolygon? Geom { get; set; }
    }
}
