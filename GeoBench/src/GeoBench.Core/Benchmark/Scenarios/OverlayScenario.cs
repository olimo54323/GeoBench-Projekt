using System.Collections.Concurrent;
using System.Globalization;
using GeoBench.Core.Data;
using GeoBench.Core.Data.Bulk;
using GeoBench.Core.Model;
using GeoBench.Core.Storage;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using NetTopologySuite.Geometries.Prepared;
using NetTopologySuite.Geometries.Utilities;
using NetTopologySuite.Index.Strtree;
using NetTopologySuite.Operation.Overlay;
using NetTopologySuite.Operation.OverlayNG;

namespace GeoBench.Core.Benchmark.Scenarios
{
    /// <summary>
    /// Scenariusze nakładania 05a–05c (powierzchnia × powierzchnia). Każde odwołanie tworzy nową instancję,
    /// bo scenariusz przechowuje ustawienia serii (naprawa geometrii).
    /// </summary>
    public static class OverlayScenarios
    {
        public static OverlayScenario<DzialkaStrefaPow> DzialkiStrefy => new(
            "05a", "Nakładanie: działki × strefy (pole przecięcia)", "05a_overlay", SpatialLayer.Dzialki, SpatialLayer.Strefy,
            (wariant, p) => new DzialkaStrefaPow { Wariant = wariant, IdDzialki = p.IdA, Oznaczenie = p.IdB, PoleM2 = p.Measure ?? 0 });

        public static OverlayScenario<BudynekStrefaPow> BudynkiStrefy => new(
            "05b", "Nakładanie: budynki × strefy (pole przecięcia, powierzchnia budynku)", "05b_overlay", SpatialLayer.Budynki, SpatialLayer.Strefy,
            (wariant, p) => new BudynekStrefaPow { Wariant = wariant, IdBudynku = p.IdA, Oznaczenie = p.IdB, PoleM2 = p.Measure ?? 0 });

        public static OverlayScenario<BudynekDzialkaPow> DzialkiBudynki => new(
            "05c", "Nakładanie: działki × budynki (pole przecięcia)", "05c_overlay", SpatialLayer.Dzialki, SpatialLayer.Budynki,
            (wariant, p) => new BudynekDzialkaPow { Wariant = wariant, IdDzialki = p.IdA, IdBudynku = p.IdB, PoleM2 = p.Measure ?? 0 });

        public static IReadOnlyList<IBenchmarkScenario> All => [DzialkiStrefy, BudynkiStrefy, DzialkiBudynki];
    }

    /// <summary>
    /// Nakładanie warstwy A na warstwę B: para (id_a, id_b, pole) powstaje, gdy geometrie się przecinają,
    /// a pole przecięcia wynosi co najmniej <see cref="MinArea"/> m² (sama styczność odpada).
    /// Niepoprawne geometrie są naprawiane albo odrzucane, zależnie od opcji serii; w NTS to osobna faza.
    /// W bazie warstwa B jest przeszukiwana indeksem przestrzennym, a A ją „przegląda”.
    /// </summary>
    public sealed class OverlayScenario<TRow> : IBenchmarkScenario where TRow : class, IWynikWariantu
    {
        /// <summary>Najmniejsze pole przecięcia pary w m² (to samo w SQL i NTS).</summary>
        public const double MinArea = 0.01;

        private readonly string _sqlBaseName;
        private readonly Func<string, ResultPair, TRow> _createRow;
        private bool _repair;

        public OverlayScenario(string id, string name, string sqlBaseName, SpatialLayer layerA, SpatialLayer layerB,
            Func<string, ResultPair, TRow> createRow)
        {
            Id = id;
            Name = name;
            _sqlBaseName = sqlBaseName;
            LayerA = layerA;
            LayerB = layerB;
            _createRow = createRow;
        }

        public string Id { get; }

        public string Name { get; }

        public string SqlFileName => _repair ? $"{_sqlBaseName}_naprawa.sql" : $"{_sqlBaseName}.sql";

        public SpatialLayer LayerA { get; }

        public SpatialLayer LayerB { get; }

        public bool HasMeasure => true;

        public bool HandlesInvalidGeometries => true;

        /// <summary>Czy bieżąca seria naprawia niepoprawne geometrie.</summary>
        public bool Repair => _repair;

        public void Configure(BenchmarkOptions options) => _repair = options.RepairInvalidGeometries;

        public INtsScenarioRun CreateNtsRun() => new Run(LayerA, LayerB, _repair);

        public IReadOnlyList<string> Describe(IReadOnlyList<ResultPair> pairs, long countA)
        {
            var withPairs = pairs.Select(p => p.IdA).Distinct(StringComparer.Ordinal).Count();
            var area = pairs.Sum(p => p.Measure ?? 0);
            return
            [
                $"pary: {pairs.Count}, {LayerA.Name} z przecięciem: {withPairs} z {countA}",
                $"suma pól przecięć: {area.ToString("N2", CultureInfo.CurrentCulture)} m²",
                _repair ? "niepoprawne geometrie naprawiane" : "niepoprawne geometrie odrzucane"
            ];
        }

        public async Task SaveResultsAsync(GeoBenchContext context, string variantCode, IReadOnlyList<ResultPair> pairs,
            CancellationToken cancellationToken = default)
        {
            var rows = pairs.Select(p => _createRow(variantCode, p)).ToList();

            await using var transaction = context.Database.CurrentTransaction == null
                ? await context.Database.BeginTransactionAsync(cancellationToken)
                : null;
            await context.Set<TRow>().Where(x => x.Wariant == variantCode).ExecuteDeleteAsync(cancellationToken);
            if (rows.Count > 0)
                await context.BulkInsertAsync(BulkTable<TRow>.FromModel(context), rows, cancellationToken);
            if (transaction != null)
                await transaction.CommitAsync(cancellationToken);
        }

        /// <summary>Stan jednego przebiegu NTS.</summary>
        private sealed class Run : INtsScenarioRun
        {
            private readonly SpatialLayer _layerA;
            private readonly SpatialLayer _layerB;
            private readonly bool _repair;
            private readonly List<string> _idsA = [];
            private readonly List<string> _idsB = [];
            // Po przygotowaniu: null = geometria odrzucona
            private readonly List<Geometry?> _geomA = [];
            private readonly List<Geometry?> _geomB = [];
            private STRtree<int>? _tree;
            private IPreparedGeometry?[] _prepared = [];

            public Run(SpatialLayer layerA, SpatialLayer layerB, bool repair)
            {
                _layerA = layerA;
                _layerB = layerB;
                _repair = repair;
            }

            public async Task LoadAsync(ISpatialStore store, CancellationToken cancellationToken)
            {
                await foreach (var feature in store.ReadFeaturesAsync(_layerA, cancellationToken))
                {
                    _idsA.Add(feature.Id);
                    _geomA.Add(feature.Geometry);
                }
                await foreach (var feature in store.ReadFeaturesAsync(_layerB, cancellationToken))
                {
                    _idsB.Add(feature.Id);
                    _geomB.Add(feature.Geometry);
                }
            }

            public bool HasPreparation => true;

            /// <summary>Kontrola poprawności każdej geometrii: naprawa (GeometryFixer) albo odrzucenie.</summary>
            public void Prepare(CancellationToken cancellationToken)
            {
                Check(_geomA, cancellationToken);
                Check(_geomB, cancellationToken);
            }

            private void Check(List<Geometry?> geometries, CancellationToken cancellationToken)
            {
                for (var i = 0; i < geometries.Count; i++)
                {
                    if ((i & 1023) == 0)
                        cancellationToken.ThrowIfCancellationRequested();
                    var geometry = geometries[i]!;
                    if (!geometry.IsValid)
                        geometries[i] = _repair ? GeometryFixer.Fix(geometry) : null;
                }
            }

            public void BuildIndex()
            {
                _prepared = new IPreparedGeometry?[_geomB.Count];
                var tree = new STRtree<int>();
                for (var i = 0; i < _geomB.Count; i++)
                {
                    var geometry = _geomB[i];
                    if (geometry == null || geometry.IsEmpty)
                        continue;
                    var prepared = PreparedGeometryFactory.Prepare(geometry);
                    // Leniwe struktury wielokąta przygotowanego budowane tutaj, a nie w wątkach obliczenia
                    if (prepared is PreparedPolygon polygon)
                    {
                        _ = polygon.IntersectionFinder;
                        _ = polygon.PointLocator;
                    }
                    _prepared[i] = prepared;
                    tree.Insert(geometry.EnvelopeInternal, i);
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
                    for (var i = 0; i < _geomA.Count; i++)
                    {
                        if ((i & 255) == 0)
                            cancellationToken.ThrowIfCancellationRequested();
                        Probe(tree, i, pairs);
                    }
                    return pairs;
                }

                var parts = new ConcurrentBag<List<ResultPair>>();
                Parallel.For(0, _geomA.Count, new ParallelOptions { CancellationToken = cancellationToken },
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
                var geometry = _geomA[a];
                if (geometry == null || geometry.IsEmpty)
                    return;
                foreach (var b in tree.Query(geometry.EnvelopeInternal))
                {
                    var prepared = _prepared[b];
                    if (prepared == null || !prepared.Intersects(geometry))
                        continue;
                    // OverlayNG z zabezpieczeniem przed błędami topologii (jak GEOS w PostGIS)
                    var area = OverlayNGRobust.Overlay(geometry, _geomB[b]!, SpatialFunction.Intersection).Area;
                    if (area >= MinArea)
                        pairs.Add(new ResultPair(_idsA[a], _idsB[b], area));
                }
            }
        }
    }
}
