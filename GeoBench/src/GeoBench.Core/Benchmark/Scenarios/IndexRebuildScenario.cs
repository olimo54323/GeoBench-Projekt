using System.Globalization;
using System.Text.RegularExpressions;
using GeoBench.Core.Data;
using GeoBench.Core.Processing;
using GeoBench.Core.Storage;
using NetTopologySuite.Geometries;
using NetTopologySuite.Index.Strtree;

namespace GeoBench.Core.Benchmark.Scenarios
{
    /// <summary>
    /// Scenariusze 2 (przebudowa indeksu przestrzennego): 02a budynki, 02b działki, 02c strefy.
    /// Każde odwołanie tworzy nowe instancje, bo scenariusz przechowuje definicje indeksów odczytane dla serii.
    /// </summary>
    public static class IndexRebuildScenarios
    {
        public static IndexRebuildScenario Budynki => new("02a", SpatialLayer.Budynki);

        public static IndexRebuildScenario Dzialki => new("02b", SpatialLayer.Dzialki);

        public static IndexRebuildScenario Strefy => new("02c", SpatialLayer.Strefy);

        public static IReadOnlyList<IndexRebuildScenario> All => [Budynki, Dzialki, Strefy];
    }

    /// <summary>
    /// Scenariusz 2: czas zbudowania od nowa indeksu przestrzennego jednej warstwy.
    /// W bazie DROP INDEX + CREATE z parametrami obecnego indeksu (SQL Server: siatka GEOMETRY_GRID z BOUNDING_BOX,
    /// poziomami i CELLS_PER_OBJECT; PostgreSQL: definicja GiST z pg_get_indexdef) w jednej transakcji.
    /// W NTS odczyt warstwy i budowa STRtree (bez fazy obliczenia). Wynik nie ma par, więc walidacja nie dotyczy;
    /// wersje wielowątkowe NTS nie dotyczą, bo STRtree buduje się jednowątkowo.
    /// </summary>
    public sealed partial class IndexRebuildScenario : IBenchmarkScenario
    {
        /// <summary>Pojemność węzła STRtree (domyślna w NTS), podawana w raporcie.</summary>
        public const int NodeCapacity = 10;

        private readonly Dictionary<DatabaseEngine, SpatialIndexInfo> _indexes = [];

        public IndexRebuildScenario(string id, SpatialLayer layer)
        {
            Id = id;
            Layer = layer;
        }

        public string Id { get; }

        public SpatialLayer Layer { get; }

        public string Name => $"Przebudowa indeksu przestrzennego: {Layer.Name}";

        public string SqlFileName => "02_rebuild_index.sql";

        public SpatialLayer LayerA => Layer;

        public SpatialLayer LayerB => Layer;

        public bool HasMeasure => false;

        public bool HandlesInvalidGeometries => false;

        public bool HasResultTable => false;

        public bool HasResultPairs => false;

        public bool HasDiagnostics => false;

        public void Configure(BenchmarkOptions options)
        {
        }

        public string? NotApplicable(BenchmarkVariant variant) =>
            variant.Strategy is NtsStrategy { Parallel: true }
                ? "STRtree w NTS buduje się jednowątkowo, więc wersja wielowątkowa nie różni się od jednowątkowej"
                : null;

        public async Task<IReadOnlyList<string>> PrepareVariantAsync(ISpatialStore store, BenchmarkVariant variant, CancellationToken cancellationToken)
        {
            if (!variant.Strategy.InDatabase)
                return [$"STRtree (NTS): pojemność węzła {NodeCapacity}, budowa jednowątkowa"];

            var index = await store.SpatialIndexAsync(Layer, cancellationToken)
                ?? throw new InvalidOperationException(
                    $"Brak indeksu przestrzennego na {Layer.Table}.{Layer.GeometryColumn} ({EngineName(store.Engine)}): zaimportuj {Layer.Name} ponownie.");
            if (store.Engine == DatabaseEngine.SqlServer
                && index.Parameters.GetValueOrDefault("tessellation_scheme") is { } scheme
                && !string.Equals(scheme, "GEOMETRY_GRID", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Indeks {index.Name} ma schemat {scheme}; przebudowa obsługuje tylko GEOMETRY_GRID.");

            _indexes[store.Engine] = index;
            return [Describe(store.Engine, index)];
        }

        /// <summary>Opis indeksu do raportu: parametry i rozmiar przed przebudową.</summary>
        public static string Describe(DatabaseEngine engine, SpatialIndexInfo index)
        {
            var p = index.Parameters;
            var size = (index.SizeBytes / 1024.0 / 1024.0).ToString("0.0", CultureInfo.InvariantCulture);
            var rows = index.Rows is { } r ? r.ToString("N0", CultureInfo.InvariantCulture) : "?";
            return engine == DatabaseEngine.SqlServer
                ? $"indeks {index.Name}: GEOMETRY_GRID {p.GetValueOrDefault("level_1")}/{p.GetValueOrDefault("level_2")}/" +
                  $"{p.GetValueOrDefault("level_3")}/{p.GetValueOrDefault("level_4")}, CELLS_PER_OBJECT {p.GetValueOrDefault("cells_per_object")}, " +
                  $"BOUNDING_BOX ({p.GetValueOrDefault("xmin")} {p.GetValueOrDefault("ymin")}, {p.GetValueOrDefault("xmax")} {p.GetValueOrDefault("ymax")}); " +
                  $"przed przebudową {size} MB, {rows} wierszy (komórek siatki)"
                : $"indeks {index.Name}: {p.GetValueOrDefault("definition")}; przed przebudową {size} MB, {rows} wpisów";
        }

        /// <summary>Szablon z wartościami w miejscu {nazwa}; nieuzupełnione miejsce to błąd (zmieniony szablon albo katalog).</summary>
        public static string FillTemplate(string template, IReadOnlyDictionary<string, string> values)
        {
            var sql = Placeholder().Replace(template, m =>
                values.TryGetValue(m.Groups[1].Value, out var value)
                    ? value
                    : throw new InvalidOperationException($"Szablon przebudowy indeksu: brak wartości dla {{{m.Groups[1].Value}}}."));
            return sql;
        }

        [GeneratedRegex(@"\{([a-z_0-9]+)\}")]
        private static partial Regex Placeholder();

        async Task<IReadOnlyList<ResultPair>> IBenchmarkScenario.ExecuteInDatabaseAsync(ISpatialStore store, string sql, CancellationToken cancellationToken)
        {
            if (!_indexes.TryGetValue(store.Engine, out var index))
                throw new InvalidOperationException("Definicja indeksu nie została odczytana (brak przygotowania wariantu).");
            await store.ExecuteDdlAsync(FillTemplate(sql, index.Parameters), cancellationToken);
            return [];
        }

        public INtsScenarioRun CreateNtsRun() => new Run(Layer);

        public IReadOnlyList<string> Describe(IReadOnlyList<ResultPair> pairs, long countA) =>
            [$"przebudowa indeksu warstwy {Layer.Name} ({Layer.Table}): {countA:N0} obiektów; wynik bez par, walidacja nie dotyczy"];

        public Task SaveResultsAsync(GeoBenchContext context, string variantCode, IReadOnlyList<ResultPair> pairs,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        private static string EngineName(DatabaseEngine engine) => engine == DatabaseEngine.SqlServer ? "SQL Server" : "PostgreSQL";

        /// <summary>Przebieg NTS: odczyt warstwy i budowa STRtree na obwiedniach geometrii.</summary>
        private sealed class Run : INtsScenarioRun
        {
            private readonly SpatialLayer _layer;
            private readonly List<Geometry> _geometries = [];

            public Run(SpatialLayer layer)
            {
                _layer = layer;
            }

            /// <summary>Zbudowane drzewo (trzymane do końca przebiegu, żeby budowa nie była pracą do wyrzucenia).</summary>
            public STRtree<int>? Tree { get; private set; }

            public async Task LoadAsync(ISpatialStore store, CancellationToken cancellationToken)
            {
                await foreach (var feature in store.ReadFeaturesAsync(_layer, cancellationToken))
                    _geometries.Add(feature.Geometry);
            }

            public bool HasPreparation => false;

            public void Prepare(CancellationToken cancellationToken)
            {
            }

            public void BuildIndex()
            {
                var tree = new STRtree<int>(NodeCapacity);
                for (var i = 0; i < _geometries.Count; i++)
                    tree.Insert(_geometries[i].EnvelopeInternal, i);
                tree.Build();
                Tree = tree;
            }

            public bool HasComputation => false;

            public IReadOnlyList<ResultPair> Compute(bool parallel, CancellationToken cancellationToken) => [];
        }
    }
}
