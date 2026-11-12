using System.Collections.Concurrent;
using GeoBench.Core.Data;
using GeoBench.Core.Data.Bulk;
using GeoBench.Core.Model;
using GeoBench.Core.Storage;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using NetTopologySuite.Geometries.Prepared;
using NetTopologySuite.Index.Strtree;

namespace GeoBench.Core.Benchmark.Scenarios
{
    /// <summary>
    /// Scenariusz 4: punkt w wielokącie. Dla każdego budynku jego centroid jest testowany względem stref
    /// planistycznych (Intersects, więc punkt na granicy dwóch stref należy do obu). Wynik: pary
    /// (id_budynku, oznaczenie strefy), zapisywane do tabeli budynki_stref.
    /// </summary>
    public sealed class PointInPolygonScenario : IBenchmarkScenario
    {
        public string Id => "04";

        public string Name => "Punkt w wielokącie: centroid budynku w strefie planistycznej";

        public string SqlFileName => "04_point_in_polygon.sql";

        public SpatialLayer LayerA => SpatialLayer.Budynki;

        public SpatialLayer LayerB => SpatialLayer.Strefy;

        public bool HasMeasure => false;

        public INtsScenarioRun CreateNtsRun() => new Run();

        public IReadOnlyList<string> Describe(IReadOnlyList<ResultPair> pairs, long countA)
        {
            var zonesPerBuilding = pairs.GroupBy(p => p.IdA, StringComparer.Ordinal)
                .Select(g => g.Select(p => p.IdB).Distinct(StringComparer.Ordinal).Count())
                .ToList();
            var inside = zonesPerBuilding.Count;
            var multiple = zonesPerBuilding.Count(c => c > 1);
            return
            [
                $"budynki w strefach: {inside} z {countA}",
                $"budynki bez strefy (centroid poza planem): {Math.Max(0, countA - inside)}",
                $"budynki w kilku strefach (centroid na granicy albo strefy nakładające się): {multiple}"
            ];
        }

        public async Task SaveResultsAsync(GeoBenchContext context, string variantCode, IReadOnlyList<ResultPair> pairs,
            CancellationToken cancellationToken = default)
        {
            var rows = pairs.Select(p => new BudynekStrefa { Wariant = variantCode, IdBudynku = p.IdA, Oznaczenie = p.IdB }).ToList();

            // Usunięcie starych wierszy wariantu i zapis nowych razem albo wcale
            await using var transaction = context.Database.CurrentTransaction == null
                ? await context.Database.BeginTransactionAsync(cancellationToken)
                : null;
            await context.BudynkiStref.Where(x => x.Wariant == variantCode).ExecuteDeleteAsync(cancellationToken);
            if (rows.Count > 0)
                await context.BulkInsertAsync(BulkTable<BudynekStrefa>.FromModel(context), rows, cancellationToken);
            if (transaction != null)
                await transaction.CommitAsync(cancellationToken);
        }

        /// <summary>Stan jednego przebiegu NTS.</summary>
        private sealed class Run : INtsScenarioRun
        {
            private readonly List<string> _buildingIds = [];
            private readonly List<Geometry> _buildings = [];
            private readonly List<string> _zoneIds = [];
            private readonly List<Geometry> _zones = [];
            private STRtree<int>? _tree;
            private IPreparedGeometry[] _prepared = [];

            public async Task LoadAsync(ISpatialStore store, CancellationToken cancellationToken)
            {
                await foreach (var feature in store.ReadFeaturesAsync(SpatialLayer.Budynki, cancellationToken))
                {
                    _buildingIds.Add(feature.Id);
                    _buildings.Add(feature.Geometry);
                }
                await foreach (var feature in store.ReadFeaturesAsync(SpatialLayer.Strefy, cancellationToken))
                {
                    _zoneIds.Add(feature.Id);
                    _zones.Add(feature.Geometry);
                }
            }

            public void BuildIndex()
            {
                _prepared = new IPreparedGeometry[_zones.Count];
                var tree = new STRtree<int>();
                for (var i = 0; i < _zones.Count; i++)
                {
                    var zone = _zones[i];
                    var prepared = PreparedGeometryFactory.Prepare(zone);
                    // Pierwsze zapytanie buduje leniwie lokalizator punktów; tu, a nie w wątkach obliczenia
                    var envelope = zone.EnvelopeInternal;
                    prepared.Intersects(zone.Factory.CreatePoint(envelope.Centre));
                    _prepared[i] = prepared;
                    tree.Insert(envelope, i);
                }
                tree.Build();
                _tree = tree;
            }

            public IReadOnlyList<ResultPair> Compute(bool parallel, CancellationToken cancellationToken)
            {
                var tree = _tree ?? throw new InvalidOperationException("Indeks nie został zbudowany.");
                if (!parallel)
                {
                    var pairs = new List<ResultPair>();
                    for (var i = 0; i < _buildings.Count; i++)
                    {
                        if ((i & 1023) == 0)
                            cancellationToken.ThrowIfCancellationRequested();
                        Probe(tree, i, pairs);
                    }
                    return pairs;
                }

                var parts = new ConcurrentBag<List<ResultPair>>();
                Parallel.For(0, _buildings.Count, new ParallelOptions { CancellationToken = cancellationToken },
                    () => new List<ResultPair>(),
                    (i, _, local) =>
                    {
                        Probe(tree, i, local);
                        return local;
                    },
                    local => parts.Add(local));
                return parts.SelectMany(p => p).ToList();
            }

            private void Probe(STRtree<int> tree, int building, List<ResultPair> pairs)
            {
                var centroid = _buildings[building].Centroid;
                if (centroid.IsEmpty)
                    return;
                foreach (var zone in tree.Query(centroid.EnvelopeInternal))
                {
                    if (_prepared[zone].Intersects(centroid))
                        pairs.Add(new ResultPair(_buildingIds[building], _zoneIds[zone]));
                }
            }
        }
    }
}
