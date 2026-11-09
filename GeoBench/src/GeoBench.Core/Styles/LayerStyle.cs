namespace GeoBench.Core.Styles
{
    /// <summary>
    /// Wygląd jednej warstwy, rodzaju strefy albo filtra. Kolory jako #RRGGBB, przezroczystość osobno.
    /// W Core, bo zapisują go zarówno ustawienia mapy, jak i pliki filtrów.
    /// </summary>
    public sealed class LayerStyle
    {
        public string FillColor { get; set; } = "#808080";

        /// <summary>Krycie wypełnienia 0–1 (0 = sam obrys).</summary>
        public double FillOpacity { get; set; } = 0.6;

        public string OutlineColor { get; set; } = "#404040";

        public double OutlineWidth { get; set; } = 1;

        public LayerStyle Clone() => (LayerStyle)MemberwiseClone();
    }
}
