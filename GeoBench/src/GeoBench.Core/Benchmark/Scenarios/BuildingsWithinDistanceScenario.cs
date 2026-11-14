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
    /// Scenariusz 07: dla każdej działki liczba budynków w odległości do <see cref="Distance"/> m (granica włącznie,
    /// odległość między powierzchniami, bez budowania bufora). Wiersz dla każdej działki, także z zerem.
    /// Para (id_dzialki, liczba jako tekst), więc liczba trafia do odcisku wyniku i walidacji.
    /// </summary>
    public sealed class BuildingsWithinDistanceScenario : IBenchmarkScenario
    {
        /// <summary>Odległość w metrach (ta sama w SQL i NTS).</summary>
        public const double Distance = 50;

        public string Id => "07";

        public string Name => "Budynki w odległości do 50 m od działki (liczba, także 0)";

        public string SqlFileName => "07_buildings_within_50m.sql";

        public SpatialLayer LayerA => SpatialLayer.Dzialki;

        public SpatialLayer LayerB => SpatialLayer.Budynki;

        public bool HasMeasure => false;

        public bool HandlesInvalidGeometries => false;

        public void Configure(BenchmarkOptions options)
        {
        }

        public INtsScenarioRun CreateNtsRun() => new Run();

        /// <summary>Liczba budynków z pary (druga kolumna wyniku).</summary>
        public static int CountOf(ResultPair pair) => int.Parse(pair.IdB, NumberStyles.Integer, CultureInfo.InvariantCulture);

        public IReadOnlyList<string> Describe(IReadOnlyList<ResultPair> pairs, long countA)
        {
            var counts = pairs.Select(CountOf).Order().ToArray();
            if (counts.Length == 0)
                return [$"działki: 0 z {countA}"];
            return
            [
                $"działki: {counts.Length} z {countA}; bez budynku w promieniu {Distance:0} m: {counts.Count(c => c == 0)}",
                $"budynków w promieniu: mediana {counts[counts.Length / 2]}, maksimum {counts[^1]}, " +
                $"razem par działka–budynek {counts.Sum(c => (long)c)}"
            ];
        }

        public async Task SaveResultsAsync(GeoBenchContext context, string variantCode, IReadOnlyList<ResultPair> pairs,
            CancellationToken cancellationToken = default)
        {
            var rows = pairs.Select(p => new DzialkaBudynki50m
            {
                Wariant = variantCode,
                IdDzialki = p.IdA,
                LiczbaBudynkow = CountOf(p)
            }).ToList();

            await using var transaction = context.Database.CurrentTransaction == null
                ? await context.Database.BeginTransactionAsync(cancellationToken)
                : null;
            await context.DzialkiBudynki50m.Where(x => x.Wariant == variantCode).ExecuteDeleteAsync(cancellationToken);
            if (rows.Count > 0)
                await context.BulkInsertAsync(BulkTable<DzialkaBudynki50m>.FromModel(context), rows, cancellationToken);
            if (transaction != null)
                await transaction.CommitAsync(cancellationToken);
        }

        /// <summary>Stan jednego przebiegu NTS: działki i budynki w pamięci, STRtree budynków.</summary>
        private sealed class Run : INtsScenarioRun
        {
            private readonly List<string> _parcelIds = [];
            private readonly List<Geometry> _parcels = [];
            private readonly List<Geometry> _buildings = [];
            private STRtree<int>? _tree;

            public async Task LoadAsync(ISpatialStore store, CancellationToken cancellationToken)
            {
                await foreach (var feature in store.ReadFeaturesAsync(SpatialLayer.Dzialki, cancellationToken))
                {
                    _parcelIds.Add(feature.Id);
                    _parcels.Add(feature.Geometry);
                }
                await foreach (var feature in store.ReadFeaturesAsync(SpatialLayer.Budynki, cancellationToken))
                    _buildings.Add(feature.Geometry);
            }

            public bool HasPreparation => false;

            public void Prepare(CancellationToken cancellationToken)
            {
            }

            public void BuildIndex()
            {
                var tree = new STRtree<int>();
                for (var i = 0; i < _buildings.Count; i++)
                {
                    if (!_buildings[i].IsEmpty)
                        tree.Insert(_buildings[i].EnvelopeInternal, i);
                }
                tree.Build();
                _tree = tree;
            }

            public IReadOnlyList<ResultPair> Compute(bool parallel, CancellationToken cancellationToken)
            {
                var tree = _tree ?? throw new InvalidOperationException("Indeks nie został zbudowany.");
                if (!parallel)
                {
                    var pairs = new List<ResultPair>(_parcels.Count);
                    for (var i = 0; i < _parcels.Count; i++)
                    {
                        if ((i & 255) == 0)
                            cancellationToken.ThrowIfCancellationRequested();
                        pairs.Add(Probe(tree, i));
                    }
                    return pairs;
                }

                var parts = new ConcurrentBag<List<ResultPair>>();
                Parallel.For(0, _parcels.Count, new ParallelOptions { CancellationToken = cancellationToken },
                    () => new List<ResultPair>(),
                    (i, _, local) =>
                    {
                        local.Add(Probe(tree, i));
                        return local;
                    },
                    local => parts.Add(local));
                return parts.SelectMany(p => p).ToList();
            }

            private ResultPair Probe(STRtree<int> tree, int a)
            {
                var parcel = _parcels[a];
                var count = 0;
                if (!parcel.IsEmpty)
                {
                    // Obwiednia powiększona o odległość: kandydaci, potem dokładny warunek odległości
                    var search = parcel.EnvelopeInternal.Copy();
                    search.ExpandBy(Distance);
                    foreach (var b in tree.Query(search))
                    {
                        if (parcel.IsWithinDistance(_buildings[b], Distance))
                            count++;
                    }
                }
                return new ResultPair(_parcelIds[a], count.ToString(CultureInfo.InvariantCulture));
            }
        }
    }
}
