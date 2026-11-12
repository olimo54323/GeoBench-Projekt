using System.Globalization;
using System.Text;
using GeoBench.Core.Benchmark;

namespace GeoBench.Core.Validation
{
    /// <summary>
    /// Wynik wzorcowy z QGIS w CSV: pierwszy wiersz to nagłówek, kolumny id_a, id_b i opcjonalnie miara
    /// (np. pole). Separator przecinek, średnik albo tabulator (rozpoznawany z nagłówka); przy średniku
    /// liczby mogą mieć przecinek dziesiętny (eksport z polskimi ustawieniami).
    /// </summary>
    public static class ReferenceResult
    {
        public static IReadOnlyList<ResultPair> Load(string path)
        {
            using var reader = new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            return Parse(reader);
        }

        public static IReadOnlyList<ResultPair> Parse(TextReader reader)
        {
            var header = reader.ReadLine() ?? throw new InvalidDataException("Plik wzorca jest pusty.");
            var separator = header.Contains('\t') ? '\t' : header.Contains(';') ? ';' : ',';

            var pairs = new List<ResultPair>();
            var lineNumber = 1;
            while (reader.ReadLine() is { } line)
            {
                lineNumber++;
                if (string.IsNullOrWhiteSpace(line))
                    continue;
                var fields = Split(line, separator);
                if (fields.Count < 2)
                    throw new InvalidDataException($"Wiersz {lineNumber}: oczekiwano co najmniej dwóch kolumn (id_a, id_b).");

                double? measure = null;
                if (fields.Count > 2 && !string.IsNullOrWhiteSpace(fields[2]))
                {
                    var text = separator == ';' ? fields[2].Replace(',', '.') : fields[2];
                    if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                        throw new InvalidDataException($"Wiersz {lineNumber}: miara „{fields[2]}” nie jest liczbą.");
                    measure = value;
                }
                pairs.Add(new ResultPair(fields[0].Trim(), fields[1].Trim(), measure));
            }
            return pairs;
        }

        /// <summary>Podział wiersza CSV z obsługą pól w cudzysłowach ("" to cudzysłów w polu).</summary>
        private static List<string> Split(string line, char separator)
        {
            var fields = new List<string>();
            var field = new StringBuilder();
            var quoted = false;
            for (var i = 0; i < line.Length; i++)
            {
                var c = line[i];
                if (quoted)
                {
                    if (c == '"' && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else if (c == '"')
                        quoted = false;
                    else
                        field.Append(c);
                }
                else if (c == '"')
                    quoted = true;
                else if (c == separator)
                {
                    fields.Add(field.ToString());
                    field.Clear();
                }
                else
                    field.Append(c);
            }
            fields.Add(field.ToString());
            return fields;
        }
    }
}
