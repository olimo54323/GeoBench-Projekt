namespace GeoBench.Core.Model
{
    /// <summary>Wynik scenariusza 06: najbliższy inny budynek (odległość powierzchni, przy remisie mniejsze id).</summary>
    public class BudynekNajblizszy : IWynikWariantu
    {
        public int Oid { get; set; }
        public required string Wariant { get; set; }
        public required string IdBudynku { get; set; }
        public required string IdSasiada { get; set; }
        public double OdlegloscM { get; set; }
    }

    /// <summary>Wynik scenariusza 07: liczba budynków w odległości do 50 m od działki (także 0).</summary>
    public class DzialkaBudynki50m : IWynikWariantu
    {
        public int Oid { get; set; }
        public required string Wariant { get; set; }
        public required string IdDzialki { get; set; }
        public int LiczbaBudynkow { get; set; }
    }

    /// <summary>Wynik scenariusza 08: niepoprawna geometria warstwy i jej pole po naprawie.</summary>
    public class GeometriaNaprawiona : IWynikWariantu
    {
        public int Oid { get; set; }
        public required string Wariant { get; set; }

        /// <summary>Tabela warstwy (budynek, dzialka, strefa_planistyczna).</summary>
        public required string Warstwa { get; set; }

        public required string IdObiektu { get; set; }
        public double PoleM2 { get; set; }
    }
}
