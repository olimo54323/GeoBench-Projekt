using System.Globalization;
using GeoBench.Core.Styles;
using Mapsui.Styles;
using Mapsui.Styles.Thematics;
using MediaColor = System.Windows.Media.Color;

namespace GeoBench.Wpf.Styles
{
    /// <summary>Zamiana ustawień (kolory #RRGGBB) na style Mapsui i kolory WPF.</summary>
    public static class MapsuiStyles
    {
        public const string SymbolKey = "symbol";

        public static VectorStyle ToVectorStyle(LayerStyle style)
        {
            var fill = ParseColor(style.FillColor);
            var outline = ParseColor(style.OutlineColor);
            return new VectorStyle
            {
                Fill = style.FillOpacity <= 0 ? null : new Brush(Color.FromArgb(Alpha(style.FillOpacity), fill.R, fill.G, fill.B)),
                Outline = style.OutlineWidth <= 0 ? null : new Pen(Color.FromArgb(255, outline.R, outline.G, outline.B), style.OutlineWidth)
            };
        }

        /// <summary>Styl stref zależny od symbolu zapisanego w obiekcie (feature["symbol"]).</summary>
        public static IStyle ZoneStyle(MapStyleSettings settings)
        {
            var styles = settings.Strefy.ToDictionary(p => p.Key, p => (IStyle)ToVectorStyle(p.Value));
            var fallback = ToVectorStyle(MapStyleSettings.DefaultStrefa(""));
            return new ThemeStyle(feature =>
                feature[SymbolKey] is string symbol && styles.TryGetValue(symbol, out var style) ? style : fallback);
        }

        public static MediaColor ParseColor(string? hex)
        {
            if (hex is { Length: 7 } && hex[0] == '#'
                && int.TryParse(hex.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
                return MediaColor.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
            return MediaColor.FromRgb(128, 128, 128);
        }

        public static string ToHex(MediaColor color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

        private static int Alpha(double opacity) => (int)Math.Round(Math.Clamp(opacity, 0, 1) * 255);
    }
}
