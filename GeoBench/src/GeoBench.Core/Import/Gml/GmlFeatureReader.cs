using System.Xml;
using System.Xml.Linq;

namespace GeoBench.Core.Import.Gml
{
    /// <summary>
    /// Strumieniowy odczyt obiektów z dokumentu GML (odpowiedź WFS albo plik): w pamięci jest naraz
    /// tylko jeden obiekt. Obsługuje wfs:member, gml:featureMember i gml:featureMembers, GML 3.1 i 3.2.
    /// Nie zakłada kolejności elementów ani wielkości liter w nazwach pól.
    /// </summary>
    public sealed class GmlFeatureReader
    {
        private static readonly XNamespace XLink = "http://www.w3.org/1999/xlink";

        private readonly GmlGeometryParser _geometryParser = new();

        public IEnumerable<FeatureRecord> Read(Stream stream)
        {
            var settings = new XmlReaderSettings
            {
                IgnoreComments = true,
                IgnoreWhitespace = true,
                IgnoreProcessingInstructions = true,
                DtdProcessing = DtdProcessing.Prohibit
            };

            using var reader = XmlReader.Create(stream, settings);
            reader.MoveToContent();
            while (!reader.EOF)
            {
                if (reader.NodeType == XmlNodeType.Element && IsMemberElement(reader.NamespaceURI, reader.LocalName))
                {
                    // ReadFrom przesuwa czytnik za wczytany element, więc bez dodatkowego Read()
                    var member = (XElement)XNode.ReadFrom(reader);
                    foreach (var feature in member.Elements())
                        yield return ReadFeature(feature);
                }
                else
                {
                    reader.Read();
                }
            }
        }

        private static bool IsMemberElement(string namespaceUri, string localName) =>
            (localName == "member" && namespaceUri.StartsWith("http://www.opengis.net/wfs", StringComparison.Ordinal))
            || ((localName == "featureMember" || localName == "featureMembers") && GmlGeometryParser.IsGmlNamespace(namespaceUri));

        private FeatureRecord ReadFeature(XElement feature)
        {
            var gmlId = feature.Attributes()
                .FirstOrDefault(a => a.Name.LocalName == "id" && GmlGeometryParser.IsGmlNamespace(a.Name.NamespaceName))?.Value;

            var properties = new List<FeatureProperty>();
            var geometries = new Dictionary<string, SourceGeometry>(StringComparer.OrdinalIgnoreCase);
            foreach (var child in feature.Elements())
                ReadProperty(child, properties, geometries);

            return new FeatureRecord(feature.Name.LocalName, gmlId, properties, geometries);
        }

        private void ReadProperty(XElement element, List<FeatureProperty> properties, Dictionary<string, SourceGeometry> geometries)
        {
            if (element.Name.LocalName == "boundedBy" && GmlGeometryParser.IsGmlNamespace(element.Name.NamespaceName))
                return;

            var geometryElement = element.Elements().FirstOrDefault(GmlGeometryParser.IsGeometryElement);
            if (geometryElement != null)
            {
                geometries[element.Name.LocalName] = _geometryParser.Parse(geometryElement);
                return;
            }

            if (element.HasElements)
            {
                // Pole złożone (np. idIIP/Identyfikator): spłaszczamy do liści
                foreach (var child in element.Elements())
                    ReadProperty(child, properties, geometries);
                return;
            }

            var value = element.Value.Trim();
            properties.Add(new FeatureProperty(
                element.Name.LocalName,
                value.Length == 0 ? null : value,
                element.Attribute(XLink + "href")?.Value,
                element.Attribute(XLink + "title")?.Value));
        }
    }
}
