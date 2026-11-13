namespace GeoBench.Core.Model
{
    /// <summary>Wiersz tabeli wyników scenariusza zapisany dla jednego wariantu (np. W1, W3P).</summary>
    public interface IWynikWariantu
    {
        string Wariant { get; set; }
    }

    /// <summary>Wynik scenariusza 05a: działka przecina strefę planistyczną, pole przecięcia ≥ 0,01 m².</summary>
    public class DzialkaStrefaPow : IWynikWariantu
    {
        public int Oid { get; set; }
        public required string Wariant { get; set; }
        public required string IdDzialki { get; set; }

        /// <summary>Oznaczenie strefy planistycznej (np. 601SU).</summary>
        public required string Oznaczenie { get; set; }

        public double PoleM2 { get; set; }
    }

    /// <summary>Wynik scenariusza 05b: budynek (powierzchnia, nie centroid) przecina strefę, pole ≥ 0,01 m².</summary>
    public class BudynekStrefaPow : IWynikWariantu
    {
        public int Oid { get; set; }
        public required string Wariant { get; set; }
        public required string IdBudynku { get; set; }
        public required string Oznaczenie { get; set; }
        public double PoleM2 { get; set; }
    }

    /// <summary>Wynik scenariusza 05c: działka przecina budynek, pole przecięcia ≥ 0,01 m².</summary>
    public class BudynekDzialkaPow : IWynikWariantu
    {
        public int Oid { get; set; }
        public required string Wariant { get; set; }
        public required string IdDzialki { get; set; }
        public required string IdBudynku { get; set; }
        public double PoleM2 { get; set; }
    }
}
