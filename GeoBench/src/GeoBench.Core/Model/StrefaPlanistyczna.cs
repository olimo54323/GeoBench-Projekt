using NetTopologySuite.Geometries;

namespace GeoBench.Core.Model
{
    /// <summary>
    /// Strefa planistyczna planu ogólnego (schemat APP 2.0). Strefy jednego aktu pokrywają gminę rozłącznie.
    /// </summary>
    public class StrefaPlanistyczna
    {
        public int Oid { get; set; }

        public int AktPlanowaniaOid { get; set; }
        public AktPlanowania AktPlanowania { get; set; } = null!;

        /// <summary>Oznaczenie strefy, unikalne w obrębie aktu (np. 601SU).</summary>
        public required string Oznaczenie { get; set; }

        /// <summary>Identyfikator lokalny z idIIP (np. 1POG-601SU).</summary>
        public required string LokalnyId { get; set; }

        /// <summary>Symbol rodzaju strefy (np. SU).</summary>
        public required string Symbol { get; set; }

        /// <summary>Rodzaj strefy z listy kodowej RodzajStrefyPlanistycznejKod (pole "nazwa" w APP).</summary>
        public RodzajStrefyPlanistycznej Nazwa { get; set; }

        public string? NazwaAlternatywna { get; set; }

        public decimal? MaksNadziemnaIntensywnoscZabudowy { get; set; }

        /// <summary>Maksymalny udział powierzchni zabudowy w procentach.</summary>
        public decimal? MaksUdzialPowierzchniZabudowy { get; set; }

        /// <summary>Maksymalna wysokość zabudowy w metrach.</summary>
        public decimal? MaksWysokoscZabudowy { get; set; }

        /// <summary>Minimalny udział powierzchni biologicznie czynnej w procentach.</summary>
        public decimal? MinUdzialPowierzchniBiologicznieCzynnej { get; set; }

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
