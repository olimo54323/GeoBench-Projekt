using GeoBench.Core.Export;
using GeoBench.Core.Storage;
using NetTopologySuite.Geometries;

namespace GeoBench.Core.Data
{
    /// <summary>
    /// Stan warstwy w jednej bazie: liczność, zasięg, niepoprawne geometrie (gdy sprawdzane), rozmiar tabeli
    /// i indeks przestrzenny. Elementy, których nie udało się odczytać, są null, a powód trafia do Error.
    /// </summary>
    public sealed record LayerOverview(
        DatabaseEngine Engine,
        SpatialLayer Layer,
        long? Count,
        Envelope? Extent,
        int? InvalidCount,
        long? TableSizeBytes,
        SpatialIndexInfo? Index,
        string? Error);

    /// <summary>Warianty zapisane w tabeli wyników jednej bazy (pusta lista: tabela bez wyników).</summary>
    public sealed record ResultTableOverview(DatabaseEngine Engine, string Table, string Title, IReadOnlyList<ExportVariant> Variants, string? Error);

    /// <summary>
    /// Zestawienie danych w bazach dla zakładki Dane: warstwy (to, czego wymaga benchmark) i wyniki scenariuszy.
    /// Błąd jednej warstwy albo tabeli nie przerywa reszty.
    /// </summary>
    public static class DataOverview
    {
        public static IReadOnlyList<SpatialLayer> Layers { get; } =
            [SpatialLayer.Budynki, SpatialLayer.Dzialki, SpatialLayer.Strefy, SpatialLayer.Regulacje];

        /// <param name="checkValidity">Sprawdzenie poprawności wszystkich geometrii (wolne: to praca scenariusza 08).</param>
        public static async Task<IReadOnlyList<LayerOverview>> LayersAsync(ISpatialStore store, bool checkValidity,
            CancellationToken cancellationToken = default)
        {
            var layers = new List<LayerOverview>();
            foreach (var layer in Layers)
                layers.Add(await LayerAsync(store, layer, checkValidity, cancellationToken));
            return layers;
        }

        public static async Task<LayerOverview> LayerAsync(ISpatialStore store, SpatialLayer layer, bool checkValidity,
            CancellationToken cancellationToken = default)
        {
            long count;
            try
            {
                count = await store.CountAsync(layer, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Zwykle brak tabeli (migracje) albo serwera: dalsze odczyty też by się nie udały
                return new LayerOverview(store.Engine, layer, null, null, null, null, null, ex.GetBaseException().Message);
            }

            var errors = new List<string>();
            async Task<T?> Try<T>(string what, Func<Task<T>> read)
            {
                try
                {
                    return await read();
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    errors.Add($"{what}: {ex.GetBaseException().Message}");
                    return default;
                }
            }

            // Zasięg pustej tabeli nie istnieje
            var extent = count > 0 ? await Try("zasięg", () => store.ExtentAsync(layer, cancellationToken)) : null;
            int? invalid = checkValidity
                ? await Try<int?>("poprawność", async () => (await store.InvalidIdsAsync(layer, cancellationToken)).Count)
                : null;
            var size = await Try<long?>("rozmiar", async () => await store.TableSizeAsync(layer, cancellationToken));
            var index = await Try("indeks", () => store.SpatialIndexAsync(layer, cancellationToken));

            return new LayerOverview(store.Engine, layer, count, extent, invalid, size, index,
                errors.Count == 0 ? null : string.Join("; ", errors));
        }

        /// <summary>Warianty w każdej tabeli wyników (z liczbą wierszy).</summary>
        public static async Task<IReadOnlyList<ResultTableOverview>> ResultsAsync(GeoBenchContext context, DatabaseEngine engine,
            CancellationToken cancellationToken = default)
        {
            var reader = new ResultExportReader(context);
            var tables = new List<ResultTableOverview>();
            foreach (var table in ResultTableCatalog.All)
            {
                var name = ResultTableCatalog.TableName(context, table);
                try
                {
                    tables.Add(new ResultTableOverview(engine, name, table.Title, await reader.VariantsAsync(table, cancellationToken), null));
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    tables.Add(new ResultTableOverview(engine, name, table.Title, [], ex.GetBaseException().Message));
                }
            }
            return tables;
        }

        /// <summary>
        /// Warstwy, których liczności różnią się między bazami (ten sam warunek co kontrola wstępna benchmarku).
        /// Warstwy odczytane tylko w jednej bazie nie są porównywane.
        /// </summary>
        public static IReadOnlyList<string> CountMismatches(IEnumerable<LayerOverview> layers) =>
            layers.Where(l => l.Count != null)
                .GroupBy(l => l.Layer)
                .Where(g => g.Select(l => l.Engine).Distinct().Count() > 1 && g.Select(l => l.Count).Distinct().Count() > 1)
                .Select(g => $"{g.Key.Name}: " + string.Join(", ", g.OrderBy(l => l.Engine).Select(l => $"{EngineName(l.Engine)} {l.Count:N0}")))
                .ToList();

        public static string EngineName(DatabaseEngine engine) => engine == DatabaseEngine.SqlServer ? "SQL Server" : "PostgreSQL";
    }
}
