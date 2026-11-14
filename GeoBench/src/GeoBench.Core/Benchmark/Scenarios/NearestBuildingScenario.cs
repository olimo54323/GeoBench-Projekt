using System.Collections.Concurrent;
using System.Globalization;
using GeoBench.Core.Data;
using GeoBench.Core.Data.Bulk;
using GeoBench.Core.Model;
using GeoBench.Core.Storage;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using NetTopologySuite.Index.Strtree;

namespace GeoBench.Core.Benchmark.Scenarios
{
    /// <summary>
    /// Scenariusz 06: dla każdego budynku najbliższy inny budynek. Para (id_budynku, id_sąsiada, odległość w m);
    /// odległość między powierzchniami, więc budynki stykające się mają 0. Przy równej odległości wygrywa mniejsze
    /// id sąsiada w porządku porządkowym (w SQL: Latin1_General_BIN2, COLLATE "C").
    /// W bazie wyszukiwanie najbliższego sąsiada z indeksem przestrzennym, w NTS STRtree k-NN
    /// z <see cref="Candidates"/> kandydatami.
    /// </summary>
    public sealed class NearestBuildingScenario : IBenchmarkScenario
    {
        /// <summary>Kandydaci k-NN w NTS (bez samego budynku); spośród nich najbliższy, przy remisie mniejsze id.</summary>
        public const int Candidates = 10;

        public string Id => "06";

        public string Name => "Najbliższy budynek (odległość powierzchni, remis: mniejsze id)";

        public string SqlFileName => "06_nearest_building.sql";

        public SpatialLayer LayerA => SpatialLayer.Budynki;

        public SpatialLayer LayerB => SpatialLayer.Budynki;

        public bool HasMeasure => true;

        public bool HandlesInvalidGeometries => false;

        public void Configure(BenchmarkOptions options)
        {
        }

        public INtsScenarioRun CreateNtsRun() => new Run();

        public IReadOnlyList<string> Describe(IReadOnlyList<ResultPair> pairs, long countA)
        {
            var distances = pairs.Select(p => p.Measure ?? 0).Order().ToArray();
            if (distances.Length == 0)
                return [$"pary: 0 z {countA} budynków"];
            var touching = distances.Count(d => d == 0);
            return
            [
                $"pary: {pairs.Count} z {countA} budynków; stykające się z sąsiadem (odległość 0): {touching}",
                $"odległość do najbliższego: mediana {distances[distances.Length / 2].ToString("N2", CultureInfo.CurrentCulture)} m, " +
                $"maksimum {distances[^1].ToString("N2", CultureInfo.CurrentCulture)} m"
            ];
        }

        public async Task SaveResultsAsync(GeoBenchContext context, string variantCode, IReadOnlyList<ResultPair> pairs,
            CancellationToken cancellationToken = default)
        {
            var rows = pairs.Select(p => new BudynekNajblizszy
            {
                Wariant = variantCode,
                IdBudynku = p.IdA,
                IdSasiada = p.IdB,
                OdlegloscM = p.Measure ?? 0
            }).ToList();

            await using var transaction = context.Database.CurrentTransaction == null
                ? await context.Database.BeginTransactionAsync(cancellationToken)
                : null;
            await context.BudynkiNajblizsze.Where(x => x.Wariant == variantCode).ExecuteDeleteAsync(cancellationToken);
            if (rows.Count > 0)
                await context.BulkInsertAsync(BulkTable<BudynekNajblizszy>.FromModel(context), rows, cancellationToken);
            if (transaction != null)
                await transaction.CommitAsync(cancellationToken);
        }

        /// <summary>Stan jednego przebiegu NTS: budynki w pamięci, STRtree i k-NN dla każdego budynku.</summary>
        private sealed class Run : INtsScenarioRun, IItemDistance<Envelope, int>
        {
            private readonly List<string> _ids = [];
            private readonly List<Geometry> _geometries = [];
            private STRtree<int>? _tree;

            public async Task LoadAsync(ISpatialStore store, CancellationToken cancellationToken)
            {
                await foreach (var feature in store.ReadFeaturesAsync(SpatialLayer.Budynki, cancellationToken))
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
                {
                    if (!_geometries[i].IsEmpty)
                        tree.Insert(_geometries[i].EnvelopeInternal, i);
                }
                tree.Build();
                _tree = tree;
            }

            /// <summary>Odległość geometrii elementów drzewa (k-NN porządkuje kandydatów według niej).</summary>
            public double Distance(IBoundable<Envelope, int> item1, IBoundable<Envelope, int> item2) =>
                _geometries[item1.Item].Distance(_geometries[item2.Item]);

            public IReadOnlyList<ResultPair> Compute(bool parallel, CancellationToken cancellationToken)
            {
                var tree = _tree ?? throw new InvalidOperationException("Indeks nie został zbudowany.");
                if (_geometries.Count < 2)
                    return [];
                if (!parallel)
                {
                    var pairs = new List<ResultPair>(_geometries.Count);
                    for (var i = 0; i < _geometries.Count; i++)
                    {
                        if ((i & 255) == 0)
                            cancellationToken.ThrowIfCancellationRequested();
                        Probe(tree, i, pairs);
                    }
                    return pairs;
                }

                var parts = new ConcurrentBag<List<ResultPair>>();
                Parallel.For(0, _geometries.Count, new ParallelOptions { CancellationToken = cancellationToken },
                    () => new List<ResultPair>(),
                    (i, _, local) =>
                    {
                        Probe(tree, i, local);
                        return local;
                    },
                    local => parts.Add(local));
                return parts.SelectMany(p => p).ToList();
            }

            private void Probe(STRtree<int> tree, int a, List<ResultPair> pairs)
            {
                var geometry = _geometries[a];
                if (geometry.IsEmpty)
                    return;

                // k + 1, bo sam budynek też jest w drzewie (odległość 0)
                var candidates = tree.NearestNeighbour(geometry.EnvelopeInternal, a, this, Candidates + 1);
                var best = -1;
                var bestDistance = double.PositiveInfinity;
                foreach (var b in candidates)
                {
                    if (b == a)
                        continue;
                    var distance = geometry.Distance(_geometries[b]);
                    if (distance < bestDistance
                        || (distance == bestDistance && string.CompareOrdinal(_ids[b], _ids[best]) < 0))
                    {
                        best = b;
                        bestDistance = distance;
                    }
                }
                if (best >= 0)
                    pairs.Add(new ResultPair(_ids[a], _ids[best], bestDistance));
            }
        }
    }
}
