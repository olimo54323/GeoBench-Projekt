using System.Collections.Concurrent;
using System.Globalization;
using GeoBench.Core.Data;
using GeoBench.Core.Storage;
using NetTopologySuite.Geometries;
using NetTopologySuite.Index.Strtree;

namespace GeoBench.Core.Benchmark.Scenarios
{
    /// <summary>
    /// Scenariusze 3 (zapytanie oknem) w trzech rozmiarach. Każde odwołanie tworzy nową instancję,
    /// bo scenariusz przechowuje okna wylosowane dla serii.
    /// </summary>
    public static class WindowQueryScenarios
    {
        /// <summary>Boki okien w metrach.</summary>
        public static IReadOnlyList<double> Sizes { get; } = [100, 500, 2000];

        public static IReadOnlyList<WindowQueryScenario> All => Sizes.Select(s => new WindowQueryScenario(s)).ToList();

        /// <summary>Rozmiar okna z identyfikatora scenariusza (03-500 → 500) albo null dla innych scenariuszy.</summary>
        public static double? SizeOf(string scenarioId) =>
            scenarioId.StartsWith(WindowQueryScenario.IdPrefix, StringComparison.Ordinal)
            && double.TryParse(scenarioId[WindowQueryScenario.IdPrefix.Length..], NumberStyles.Float, CultureInfo.InvariantCulture, out var size)
                ? size
                : null;
    }

    /// <summary>
    /// Scenariusz 3: zapytanie oknem. <see cref="WindowCount"/> kwadratowych okien o boku <see cref="Size"/> m losowanych
    /// z ustalonym ziarnem w zasięgu warstwy działek (całe okno w zasięgu). Te same środki okien dla wszystkich rozmiarów,
    /// więc okna różnych rozmiarów są zagnieżdżone. W bazie osobne zapytanie z parametrami na każde okno (polecenie
    /// przygotowane raz), w NTS jeden odczyt warstwy, STRtree i zapytania do drzewa. Para: (okno, id_dzialki).
    /// </summary>
    public sealed class WindowQueryScenario : IBenchmarkScenario
    {
        public const string IdPrefix = "03-";
        public const int WindowCount = 100;
        public const int Seed = 2180;

        private IReadOnlyList<Window> _windows = [];

        public WindowQueryScenario(double size)
        {
            if (size <= 0)
                throw new ArgumentOutOfRangeException(nameof(size));
            Size = size;
        }

        /// <summary>Bok okna w metrach.</summary>
        public double Size { get; }

        public string Id => IdPrefix + Size.ToString("0", CultureInfo.InvariantCulture);

        public string Name => $"Zapytanie oknem: {WindowCount} okien {Size.ToString("0", CultureInfo.InvariantCulture)} × {Size.ToString("0", CultureInfo.InvariantCulture)} m (działki)";

        public string SqlFileName => "03_window_query.sql";

        public SpatialLayer LayerA => SpatialLayer.Dzialki;

        public SpatialLayer LayerB => SpatialLayer.Dzialki;

        public bool HasMeasure => false;

        public bool HandlesInvalidGeometries => false;

        public bool HasResultTable => false;

        /// <summary>Okna bieżącej serii (po przygotowaniu).</summary>
        public IReadOnlyList<Window> Windows => _windows;

        /// <summary>Zasięg, w którym losowano okna.</summary>
        public Envelope? Extent { get; private set; }

        public void Configure(BenchmarkOptions options)
        {
        }

        public async Task PrepareAsync(ISpatialStore store, CancellationToken cancellationToken)
        {
            var extent = await store.ExtentAsync(LayerA, cancellationToken);
            UseExtent(extent);
        }

        /// <summary>Losuje okna w zasięgu (ziarno stałe, więc wynik powtarzalny).</summary>
        public void UseExtent(Envelope extent)
        {
            Extent = extent;
            _windows = Generate(extent, Size, WindowCount, Seed);
        }

        public static IReadOnlyList<Window> Generate(Envelope extent, double size, int count, int seed)
        {
            // Środki tak, żeby okno największego rozmiaru mieściło się w zasięgu: te same środki dla każdego rozmiaru
            var margin = Math.Min(WindowQueryScenarios.Sizes.Max(), Math.Min(extent.Width, extent.Height)) / 2;
            if (size / 2 > margin)
                margin = size / 2;
            var random = new Random(seed);
            var windows = new List<Window>(count);
            for (var i = 0; i < count; i++)
            {
                var cx = extent.MinX + margin + random.NextDouble() * Math.Max(0, extent.Width - 2 * margin);
                var cy = extent.MinY + margin + random.NextDouble() * Math.Max(0, extent.Height - 2 * margin);
                windows.Add(new Window($"okno{(i + 1).ToString("000", CultureInfo.InvariantCulture)}",
                    new Envelope(cx - size / 2, cx + size / 2, cy - size / 2, cy + size / 2)));
            }
            return windows;
        }

        Task<IReadOnlyList<ResultPair>> IBenchmarkScenario.ExecuteInDatabaseAsync(ISpatialStore store, string sql, CancellationToken cancellationToken) =>
            store.ExecuteParameterizedAsync(sql, RequireWindows().Select(w => new ParameterSet(w.Id, Parameters(w.Envelope))).ToList(), cancellationToken);

        /// <summary>Zapytanie pierwszego okna z wartościami wstawionymi w miejsce parametrów (plan zapytania).</summary>
        public string DiagnosticSql(DatabaseEngine engine)
        {
            var sql = ScenarioSql.Load(engine, SqlFileName);
            foreach (var (name, value) in Parameters(RequireWindows()[0].Envelope))
                sql = sql.Replace("@" + name, value.ToString("R", CultureInfo.InvariantCulture), StringComparison.Ordinal);
            return sql;
        }

        private static IReadOnlyList<KeyValuePair<string, double>> Parameters(Envelope e) =>
        [
            new("xmin", e.MinX), new("ymin", e.MinY), new("xmax", e.MaxX), new("ymax", e.MaxY)
        ];

        private IReadOnlyList<Window> RequireWindows() =>
            _windows.Count > 0 ? _windows : throw new InvalidOperationException("Okna nie zostały wylosowane (brak przygotowania serii).");

        public INtsScenarioRun CreateNtsRun() => new Run(RequireWindows());

        public IReadOnlyList<string> Describe(IReadOnlyList<ResultPair> pairs, long countA)
        {
            var perWindow = _windows.Select(w => 0).ToArray();
            var index = _windows.Select((w, i) => (w.Id, i)).ToDictionary(x => x.Id, x => x.i);
            foreach (var pair in pairs)
                if (index.TryGetValue(pair.IdA, out var i))
                    perWindow[i]++;
            Array.Sort(perWindow);
            var extent = Extent is { } e
                ? $"{e.MinX.ToString("0", CultureInfo.InvariantCulture)} {e.MinY.ToString("0", CultureInfo.InvariantCulture)}, " +
                  $"{e.MaxX.ToString("0", CultureInfo.InvariantCulture)} {e.MaxY.ToString("0", CultureInfo.InvariantCulture)}"
                : "?";
            return
            [
                $"okna: {_windows.Count} × {Size.ToString("0", CultureInfo.InvariantCulture)} m, ziarno {Seed}, zasięg {extent}",
                perWindow.Length == 0
                    ? "działki w oknie: brak okien"
                    : $"działki w oknie: min {perWindow[0]}, mediana {perWindow[perWindow.Length / 2]}, max {perWindow[^1]}, razem {pairs.Count} z {countA}"
            ];
        }

        public Task SaveResultsAsync(GeoBenchContext context, string variantCode, IReadOnlyList<ResultPair> pairs,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        /// <summary>Okno zapytania z identyfikatorem do par wyniku.</summary>
        public sealed record Window(string Id, Envelope Envelope);

        /// <summary>Przebieg NTS: cała warstwa w pamięci, STRtree, zapytanie drzewa i dokładny test dla każdego okna.</summary>
        private sealed class Run : INtsScenarioRun
        {
            private readonly IReadOnlyList<Window> _windows;
            private readonly List<string> _ids = [];
            private readonly List<Geometry> _geometries = [];
            private STRtree<int>? _tree;

            public Run(IReadOnlyList<Window> windows)
            {
                _windows = windows;
            }

            public async Task LoadAsync(ISpatialStore store, CancellationToken cancellationToken)
            {
                await foreach (var feature in store.ReadFeaturesAsync(SpatialLayer.Dzialki, cancellationToken))
                {
                    _ids.Add(feature.Id);
                    _geometries.Add(feature.Geometry);
                }
            }

            public bool HasPreparation => false;

            public void Prepare(CancellationToken cancellationToken)
            {
            }

            public void BuildIndex()
            {
                var tree = new STRtree<int>();
                for (var i = 0; i < _geometries.Count; i++)
                    tree.Insert(_geometries[i].EnvelopeInternal, i);
                tree.Build();
                _tree = tree;
            }

            public IReadOnlyList<ResultPair> Compute(bool parallel, CancellationToken cancellationToken)
            {
                var tree = _tree ?? throw new InvalidOperationException("Indeks nie został zbudowany.");
                if (!parallel)
                {
                    var pairs = new List<ResultPair>();
                    foreach (var window in _windows)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        Query(tree, window, pairs);
                    }
                    return pairs;
                }

                var parts = new ConcurrentBag<List<ResultPair>>();
                Parallel.ForEach(_windows, new ParallelOptions { CancellationToken = cancellationToken },
                    () => new List<ResultPair>(),
                    (window, _, local) =>
                    {
                        Query(tree, window, local);
                        return local;
                    },
                    local => parts.Add(local));
                return parts.SelectMany(p => p).ToList();
            }

            private void Query(STRtree<int> tree, Window window, List<ResultPair> pairs)
            {
                var factory = _geometries.Count > 0 ? _geometries[0].Factory : NetTopologySuite.NtsGeometryServices.Instance.CreateGeometryFactory(GeoBenchContext.Srid);
                // Prostokąt: Intersects korzysta z szybkiej ścieżki RectangleIntersects
                var rectangle = factory.ToGeometry(window.Envelope);
                foreach (var i in tree.Query(window.Envelope))
                {
                    if (rectangle.Intersects(_geometries[i]))
                        pairs.Add(new ResultPair(window.Id, _ids[i]));
                }
            }
        }
    }
}
