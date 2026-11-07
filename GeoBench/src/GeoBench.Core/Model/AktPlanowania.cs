using NetTopologySuite.Geometries;

namespace GeoBench.Core.Model
{
    /// <summary>
    /// Akt planowania przestrzennego (schemat APP 2.0), np. plan ogólny gminy.
    /// </summary>
    public class AktPlanowania
    {
        public int Oid { get; set; }

        /// <summary>Przestrzeń nazw z idIIP (np. PL.ZIPPZP.9398/246401-POG).</summary>
        public required string PrzestrzenNazw { get; set; }

        /// <summary>Identyfikator lokalny z idIIP (np. 1POG).</summary>
        public required string LokalnyId { get; set; }

        public required string Tytul { get; set; }
        public string? TytulAlternatywny { get; set; }

        /// <summary>Kod statusu z listy INSPIRE ProcessStepGeneralValue (np. elaboration).</summary>
        public string? Status { get; set; }

        public DateOnly? ObowiazujeOd { get; set; }

        /// <summary>Plan nieobowiązujący, traktowany wyłącznie jako dane badawcze (np. POG Częstochowy).</summary>
        public bool DaneBadawcze { get; set; }

        /// <summary>Zasięg przestrzenny aktu (granica gminy) w EPSG:2180.</summary>
        public MultiPolygon? Zasieg { get; set; }

        public List<StrefaPlanistyczna> Strefy { get; set; } = [];
    }
}
