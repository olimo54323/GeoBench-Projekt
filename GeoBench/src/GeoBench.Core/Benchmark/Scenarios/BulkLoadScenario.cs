using System.Globalization;
using GeoBench.Core.Data;
using GeoBench.Core.Data.Bulk;
using GeoBench.Core.Import.Egib;
using GeoBench.Core.Model;
using GeoBench.Core.Storage;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;

namespace GeoBench.Core.Benchmark.Scenarios
{
    /// <summary>
    /// Scenariusze 1 (ładowanie masowe): 01a budynki, 01b działki, 01c strefy.
    /// Każde odwołanie tworzy nowe instancje, bo scenariusz przechowuje wiersze odczytane dla serii.
    /// </summary>
    public static class BulkLoadScenarios
    {
        public static IBenchmarkScenario Budynki =>
            new BulkLoadScenario<Budynek>("01a", SpatialLayer.Budynki, b => b.Geom, nameof(Budynek.Geom));

        public static IBenchmarkScenario Dzialki =>
            new BulkLoadScenario<Dzialka>("01b", SpatialLayer.Dzialki, d => d.Geom, nameof(Dzialka.Geom));

        public static IBenchmarkScenario Strefy =>
            new BulkLoadScenario<StrefaPlanistyczna>("01c", SpatialLayer.Strefy, s => s.Geom, nameof(StrefaPlanistyczna.Geom));

        public static IReadOnlyList<IBenchmarkScenario> All => [Budynki, Dzialki, Strefy];
    }

    /// <summary>
    /// Scenariusz 1: ponowne załadowanie tabeli warstwy z pamięci tą samą ścieżką co import (bez czytania GML i mapowania).
    /// Poza pomiarem (przygotowanie wariantu): odczyt wszystkich wierszy warstwy z bazy wariantu, z atrybutami, i ich zasięgu.
    /// Mierzone, w jednej transakcji zakończonej zatwierdzeniem: usunięcie indeksu przestrzennego, TRUNCATE, zapis masowy
    /// paczkami jak w imporcie (SqlBulkCopy / binarny COPY) i budowa indeksu przestrzennego z odświeżeniem statystyk.
    /// Po przebiegu tabela ma te same dane (klucze techniczne Oid mogą się przenumerować; tabele wyników łączą się
    /// po identyfikatorach biznesowych). Przebieg przerwany błędem wycofuje transakcję.
    /// Warianty NTS nie dotyczą: ładowanie to zapis do bazy, NTS nie ma tu odpowiednika.
    /// </summary>
    public sealed class BulkLoadScenario<TEntity> : IBenchmarkScenario where TEntity : class
    {
        private readonly Func<TEntity, Geometry?> _geometry;
        private readonly string _geometryProperty;
        private readonly Dictionary<DatabaseEngine, (List<TEntity> Rows, Envelope Extent)> _data = [];

        public BulkLoadScenario(string id, SpatialLayer layer, Func<TEntity, Geometry?> geometry, string geometryProperty)
        {
            Id = id;
            Layer = layer;
            _geometry = geometry;
            _geometryProperty = geometryProperty;
        }

        public string Id { get; }

        public SpatialLayer Layer { get; }

        public string Name => $"Ładowanie masowe: {Layer.Name}";

        public string SqlFileName => "01_bulk_load.sql";

        public SpatialLayer LayerA => Layer;

        public SpatialLayer LayerB => Layer;

        public bool HasMeasure => false;

        public bool HandlesInvalidGeometries => false;

        public bool HasResultTable => false;

        public bool HasResultPairs => false;

        public bool HasDiagnostics => false;

        /// <summary>Liczba wierszy w paczce zapisu masowego (ta sama co w imporcie EGiB).</summary>
        public static int BatchSize => EgibImporter.BatchSize;

        public void Configure(BenchmarkOptions options)
        {
        }

        public string? NotApplicable(BenchmarkVariant variant) =>
            variant.Strategy.InDatabase
                ? null
                : "ładowanie masowe to zapis do bazy; przetwarzanie w NTS nie ma tu odpowiednika";

        public async Task<IReadOnlyList<string>> PrepareVariantAsync(ISpatialStore store, BenchmarkVariant variant, CancellationToken cancellationToken)
        {
            if (!variant.Strategy.InDatabase)
                return [];

            // Wiersze w kolejności klucza, żeby każdy przebieg ładował je tak samo
            var rows = await store.Context.Set<TEntity>().AsNoTracking()
                .OrderBy(e => EF.Property<int>(e, "Oid"))
                .ToListAsync(cancellationToken);
            if (rows.Count == 0)
                throw new InvalidOperationException($"Tabela {Layer.Table} jest pusta: zaimportuj {Layer.Name} przed pomiarem ładowania.");

            var extent = Extent(rows);
            _data[store.Engine] = (rows, extent);

            var size = await store.TableSizeAsync(Layer, cancellationToken);
            var index = await store.SpatialIndexAsync(Layer, cancellationToken);
            var withGeometry = rows.Count(r => _geometry(r) != null);
            return
            [
                $"źródło: {rows.Count:N0} wierszy {Layer.Table} odczytanych z bazy przed pomiarem ({withGeometry:N0} z geometrią); " +
                $"tabela przed pomiarem {Mb(size)} MB",
                $"zapis masowy paczkami po {BatchSize:N0} ({(store.Engine == DatabaseEngine.SqlServer ? "SqlBulkCopy z TABLOCK" : "binarny COPY")}), " +
                "potem budowa indeksu przestrzennego i odświeżenie statystyk; całość w jednej transakcji",
                index == null
                    ? "przed pomiarem tabela nie miała indeksu przestrzennego"
                    : $"indeks przed pomiarem: {index.Name}, {Mb(index.SizeBytes)} MB"
            ];
        }

        async Task<IReadOnlyList<ResultPair>> IBenchmarkScenario.ExecuteInDatabaseAsync(ISpatialStore store, string sql, CancellationToken cancellationToken)
        {
            if (!_data.TryGetValue(store.Engine, out var data))
                throw new InvalidOperationException("Wiersze warstwy nie zostały odczytane (brak przygotowania wariantu).");

            var context = store.Context;
            var table = BulkTable<TEntity>.FromModel(context);

            // Transakcja wywołującego (np. test wycofujący zmiany) obejmuje całe ładowanie; inaczej własna, zatwierdzana w pomiarze
            await using var transaction = context.Database.CurrentTransaction == null
                ? await context.Database.BeginTransactionAsync(cancellationToken)
                : null;

            await context.DropSpatialIndexAsync<TEntity>(_geometryProperty, cancellationToken);
            await context.TruncateAsync<TEntity>(cancellationToken);
            foreach (var batch in data.Rows.Chunk(BatchSize))
                await context.BulkInsertAsync(table, batch, cancellationToken);
            if (!data.Extent.IsNull && data.Extent.Width > 0 && data.Extent.Height > 0)
                await context.CreateSpatialIndexAsync<TEntity>(_geometryProperty, data.Extent, cancellationToken);

            if (transaction != null)
                await transaction.CommitAsync(cancellationToken);
            return [];
        }

        public INtsScenarioRun CreateNtsRun() =>
            throw new NotSupportedException("Ładowanie masowe nie ma przebiegu NTS.");

        public IReadOnlyList<string> Describe(IReadOnlyList<ResultPair> pairs, long countA) =>
            [$"ładowanie masowe warstwy {Layer.Name} ({Layer.Table}): {countA:N0} obiektów z geometrią; wynik bez par, walidacja nie dotyczy"];

        public Task SaveResultsAsync(GeoBenchContext context, string variantCode, IReadOnlyList<ResultPair> pairs,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        /// <summary>Zasięg geometrii wierszy (BOUNDING_BOX indeksu siatkowego SQL Server).</summary>
        private Envelope Extent(IEnumerable<TEntity> rows)
        {
            var extent = new Envelope();
            foreach (var row in rows)
            {
                if (_geometry(row) is { } geometry)
                    extent.ExpandToInclude(geometry.EnvelopeInternal);
            }
            return extent;
        }

        private static string Mb(long bytes) => (bytes / 1024.0 / 1024.0).ToString("0.0", CultureInfo.InvariantCulture);
    }
}
