using System.Globalization;
using System.Net;
using System.Xml;

namespace GeoBench.Core.Import.Wfs
{
    /// <summary>Błąd zgłoszony przez usługę (ows:ExceptionReport) albo niespójna odpowiedź; ponawianie nie pomoże.</summary>
    public sealed class WfsException(string message) : Exception(message);

    /// <summary>
    /// Żądania GetFeature (WFS 2.0) ze stronicowaniem STARTINDEX + COUNT i stałym sortowaniem.
    /// Strona trafia na dysk przez plik tymczasowy i jest sprawdzana przed podmianą, więc przerwane
    /// pobieranie nie zostawia uszkodzonego pliku. Błędy sieci i serwera (5xx) są ponawiane z rosnącym opóźnieniem.
    /// </summary>
    public sealed class WfsClient
    {
        private static readonly TimeSpan[] RetryDelays = [TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15)];

        private readonly HttpClient _http;

        public WfsClient(HttpClient http)
        {
            _http = http;
        }

        /// <summary>HttpClient z czasem oczekiwania dopasowanym do dużych stron (tysiące obiektów).</summary>
        public static HttpClient CreateHttpClient() => new() { Timeout = TimeSpan.FromMinutes(5) };

        public static Uri BuildGetFeatureUri(WfsSourceOptions source, WfsLayerOptions layer, int startIndex, int count)
        {
            var parameters = new List<string>
            {
                "SERVICE=WFS",
                $"VERSION={Escape(source.Version)}",
                "REQUEST=GetFeature",
                $"TYPENAMES={Escape(layer.TypeName)}",
                $"SRSNAME={Escape(source.SrsName)}",
                $"COUNT={count.ToString(CultureInfo.InvariantCulture)}",
                $"STARTINDEX={startIndex.ToString(CultureInfo.InvariantCulture)}"
            };
            if (!string.IsNullOrWhiteSpace(source.Namespaces))
                parameters.Add($"NAMESPACES={Escape(source.Namespaces)}");
            if (!string.IsNullOrWhiteSpace(layer.SortBy))
                parameters.Add($"SORTBY={Escape(layer.SortBy)}");
            if (!string.IsNullOrWhiteSpace(source.ExtraQuery))
                parameters.Add(source.ExtraQuery.Trim('&', '?'));

            var separator = source.Url.Contains('?') ? "&" : "?";
            return new Uri(source.Url + separator + string.Join("&", parameters));
        }

        /// <summary>Liczba obiektów warstwy (numberMatched) z odpowiedzi na żądanie jednego obiektu.</summary>
        public async Task<int> GetNumberMatchedAsync(WfsSourceOptions source, WfsLayerOptions layer, CancellationToken cancellationToken)
        {
            var uri = BuildGetFeatureUri(source, layer, 0, 1);
            return await WithRetryAsync(async () =>
            {
                using var response = await _http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                response.EnsureSuccessStatusCode();
                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var reader = CreateReader(stream);
                ThrowIfNotFeatureCollection(reader);

                var matched = reader.GetAttribute("numberMatched");
                if (!int.TryParse(matched, NumberStyles.None, CultureInfo.InvariantCulture, out var number))
                    throw new WfsException($"Usługa nie podaje liczby obiektów (numberMatched = {matched ?? "brak"}); stronicowanie wymaga tej liczby.");
                return number;
            }, cancellationToken);
        }

        /// <summary>
        /// Pobiera stronę do pliku. Zwraca liczbę obiektów; mniej niż oczekiwano (poza ostatnią stroną)
        /// oznacza, że serwer obciął stronę, i jest błędem, bo zgubilibyśmy obiekty.
        /// </summary>
        public Task<int> DownloadPageAsync(Uri uri, string path, int expectedCount, CancellationToken cancellationToken) =>
            WithRetryAsync(async () =>
            {
                var temp = path + ".tmp";
                using (var response = await _http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
                {
                    response.EnsureSuccessStatusCode();
                    await using var file = File.Create(temp);
                    await response.Content.CopyToAsync(file, cancellationToken);
                }

                var returned = ValidatePage(temp);
                if (returned < expectedCount)
                    throw new WfsException($"Serwer zwrócił {returned} obiektów zamiast {expectedCount} ({uri}). Zmniejsz PageSize w sources.json.");
                File.Move(temp, path, overwrite: true);
                return returned;
            }, cancellationToken);

        /// <summary>Sprawdza, że plik jest kompletnym FeatureCollection, i zwraca numberReturned.</summary>
        public static int ValidatePage(string path)
        {
            using var stream = File.OpenRead(path);
            using var reader = CreateReader(stream);
            ThrowIfNotFeatureCollection(reader);
            var returned = reader.GetAttribute("numberReturned");
            // Odczyt do końca wykrywa ucięty dokument (XmlException jest ponawiany)
            while (reader.Read())
            {
            }
            return int.TryParse(returned, NumberStyles.None, CultureInfo.InvariantCulture, out var number) ? number : 0;
        }

        private static string Escape(string value) => Uri.EscapeDataString(value);

        private static XmlReader CreateReader(Stream stream) =>
            XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, IgnoreWhitespace = true, Async = false });

        private static void ThrowIfNotFeatureCollection(XmlReader reader)
        {
            reader.MoveToContent();
            if (reader.LocalName == "FeatureCollection")
                return;
            if (reader.LocalName == "ExceptionReport")
            {
                var text = reader.ReadOuterXml();
                var message = System.Xml.Linq.XElement.Parse(text).Value.Trim();
                throw new WfsException($"Usługa zgłosiła błąd: {message}");
            }
            throw new WfsException($"Nieoczekiwana odpowiedź usługi: element {reader.Name}.");
        }

        private static async Task<T> WithRetryAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken)
        {
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    return await operation();
                }
                catch (Exception ex) when (attempt < RetryDelays.Length && IsTransient(ex, cancellationToken))
                {
                    await Task.Delay(RetryDelays[attempt], cancellationToken);
                }
            }
        }

        private static bool IsTransient(Exception ex, CancellationToken cancellationToken) => ex switch
        {
            HttpRequestException http => http.StatusCode is null
                || (int)http.StatusCode >= 500
                || http.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.RequestTimeout,
            // Przekroczony czas HttpClient (nie anulowanie przez użytkownika)
            TaskCanceledException => !cancellationToken.IsCancellationRequested,
            IOException or XmlException => true,
            _ => false
        };
    }
}
