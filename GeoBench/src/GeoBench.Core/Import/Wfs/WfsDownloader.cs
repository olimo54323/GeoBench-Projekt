namespace GeoBench.Core.Import.Wfs
{
    /// <summary>
    /// Faza A importu: warstwa WFS do cache. Liczba stron wynika z numberMatched, strony są pobierane
    /// równolegle (najwyżej MaxParallelRequests naraz), a przy kolejnym uruchomieniu dociągane są tylko brakujące.
    /// </summary>
    public sealed class WfsDownloader
    {
        private readonly WfsClient _client;
        private readonly WfsCache _cache;

        public WfsDownloader(WfsClient client, WfsCache cache)
        {
            _client = client;
            _cache = cache;
        }

        /// <param name="refresh">Pobiera wszystkie strony od nowa (nowy manifest), zamiast korzystać z cache.</param>
        public async Task<WfsManifest> EnsureDownloadedAsync(WfsSourceOptions source, WfsLayerOptions layer, bool refresh,
            IProgress<ImportProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            var stage = $"Pobieranie {layer.Title ?? layer.Name}";
            var manifest = refresh ? null : _cache.ReadManifest(source, layer);
            IReadOnlyList<int> pages;

            if (manifest == null || !manifest.Matches(source, layer))
            {
                var matched = await _client.GetNumberMatchedAsync(source, layer, cancellationToken);
                manifest = new WfsManifest
                {
                    Source = source.Name,
                    Layer = layer.Name,
                    TypeName = layer.TypeName,
                    SrsName = source.SrsName,
                    SortBy = layer.SortBy,
                    NumberMatched = matched,
                    PageSize = source.PageSize,
                    CreatedUtc = DateTime.UtcNow
                };
                _cache.WriteManifest(source, layer, manifest);
                pages = Enumerable.Range(0, manifest.PageCount).ToList();
            }
            else
            {
                pages = _cache.MissingPages(source, layer, manifest);
            }

            var total = manifest.PageCount;
            var done = total - pages.Count;
            progress?.Report(new ImportProgress(stage, done, total));

            using var gate = new SemaphoreSlim(Math.Max(1, source.MaxParallelRequests));
            var tasks = pages.Select(async page =>
            {
                await gate.WaitAsync(cancellationToken);
                try
                {
                    var uri = WfsClient.BuildGetFeatureUri(source, layer, page * manifest.PageSize, manifest.PageSize);
                    await _client.DownloadPageAsync(uri, _cache.PagePath(source, layer, page), manifest.ExpectedCount(page), cancellationToken);
                    progress?.Report(new ImportProgress(stage, Interlocked.Increment(ref done), total));
                }
                finally
                {
                    gate.Release();
                }
            });
            await Task.WhenAll(tasks);
            return manifest;
        }
    }
}
