using NetTopologySuite.Geometries;

namespace GeoBench.Core.Import
{
    /// <summary>
    /// Właściwość obiektu ze źródła: tekst elementu albo odnośnik xlink (href i title).
    /// </summary>
    public sealed record FeatureProperty(string Name, string? Value, string? Href, string? Title);

    /// <summary>
    /// Geometria odczytana ze źródła: współrzędne w kolejności osi z pliku, przed transformacją.
    /// </summary>
    public sealed record SourceGeometry(Geometry Geometry, string? SrsName, int ArcCount);

    /// <summary>
    /// Obiekt odczytany ze źródła (GML z pliku lub z WFS), niezależny od encji docelowej.
    /// Nazwy właściwości są porównywane bez rozróżniania wielkości liter, bo usługi różnią się zapisem
    /// (ID_BUDYNKU, id_budynku). Zagnieżdżone pola (idIIP/Identyfikator/lokalnyId) są dostępne po nazwie liścia.
    /// </summary>
    public sealed class FeatureRecord
    {
        public FeatureRecord(string typeName, string? gmlId, IReadOnlyList<FeatureProperty> properties,
            IReadOnlyDictionary<string, SourceGeometry> geometries)
        {
            TypeName = typeName;
            GmlId = gmlId;
            Properties = properties;
            Geometries = geometries;
        }

        /// <summary>Nazwa lokalna elementu obiektu (np. dzialki, StrefaPlanistyczna).</summary>
        public string TypeName { get; }

        public string? GmlId { get; }

        public IReadOnlyList<FeatureProperty> Properties { get; }

        /// <summary>Geometrie według nazwy właściwości (np. geom, geometria, zasiegPrzestrzenny).</summary>
        public IReadOnlyDictionary<string, SourceGeometry> Geometries { get; }

        public FeatureProperty? Find(string name) =>
            Properties.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

        public IEnumerable<FeatureProperty> FindAll(string name) =>
            Properties.Where(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

        /// <summary>Tekst właściwości; null, gdy jej brak albo jest pusta.</summary>
        public string? GetValue(string name) => Find(name)?.Value;

        public SourceGeometry? GetGeometry(string name) => Geometries.GetValueOrDefault(name);
    }
}
