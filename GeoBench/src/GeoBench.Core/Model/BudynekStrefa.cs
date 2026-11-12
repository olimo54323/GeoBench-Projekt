namespace GeoBench.Core.Model
{
    /// <summary>
    /// Wynik scenariusza 4 (punkt w wielokącie): budynek, którego centroid leży w strefie planistycznej.
    /// Powiązanie po identyfikatorach biznesowych, osobno dla każdego wariantu (W1, W2, W3, …).
    /// </summary>
    public class BudynekStrefa
    {
        public int Oid { get; set; }

        /// <summary>Kod wariantu, który dał wynik (np. W1, W3P).</summary>
        public required string Wariant { get; set; }

        public required string IdBudynku { get; set; }

        /// <summary>Oznaczenie strefy planistycznej (np. 601SU).</summary>
        public required string Oznaczenie { get; set; }
    }
}
