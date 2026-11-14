using System.Collections.Concurrent;
using System.Globalization;
using GeoBench.Core.Data;
using GeoBench.Core.Data.Bulk;
using GeoBench.Core.Model;
using GeoBench.Core.Storage;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using NetTopologySuite.Geometries.Utilities;

namespace GeoBench.Core.Benchmark.Scenarios
{
    /// <summary>
    /// Scenariusz 08: walidacja wszystkich geometrii trzech warstw (budynki, działki, strefy) i naprawa niepoprawnych.
    /// Wynik tylko dla niepoprawnych: para (warstwa = nazwa tabeli, identyfikator obiektu, pole po naprawie w m²).
    /// W bazie STIsValid + MakeValid / ST_IsValid + ST_MakeValid, w NTS IsValid + GeometryFixer. Walidatory i algorytmy
    /// naprawy silników różnią się, więc różnice par albo sumy pól są wynikiem porównania, a nie błędem.
    /// </summary>
    public sealed class GeometryValidationScenario : IBenchmarkScenario
    {
        /// <summary>Sprawdzane warstwy w kolejności wyniku.</summary>
        public static IReadOnlyList<SpatialLayer> CheckedLayers { get; } = [SpatialLayer.Budynki, SpatialLayer.Dzialki, SpatialLayer.Strefy];

        public string Id => "08";

        public string Name => "Walidacja i naprawa geometrii (budynki, działki, strefy)";

        public string SqlFileName => "08_validate_geometries.sql";

        public SpatialLayer LayerA => SpatialLayer.Budynki;

        public SpatialLayer LayerB => SpatialLayer.Dzialki;

        public IReadOnlyList<SpatialLayer> Layers => CheckedLayers;

        public bool HasMeasure => true;

        // Scenariusz sam szuka niepoprawnych geometrii (to mierzona praca), bez kontroli wstępnej i opcji naprawy
        public bool HandlesInvalidGeometries => false;

        public void Configure(BenchmarkOptions options)
        {
        }

        public INtsScenarioRun CreateNtsRun() => new Run();

        public IReadOnlyList<string> Describe(IReadOnlyList<ResultPair> pairs, long countA)
        {
            var perLayer = CheckedLayers.Select(l => $"{l.Table} {pairs.Count(p => p.IdA == l.Table)}");
            var area = pairs.Sum(p => p.Measure ?? 0);
            return
            [
                $"niepoprawne geometrie: {pairs.Count} ({string.Join(", ", perLayer)})",
                $"suma pól po naprawie: {area.ToString("N2", CultureInfo.CurrentCulture)} m²"
            ];
        }

        public async Task SaveResultsAsync(GeoBenchContext context, string variantCode, IReadOnlyList<ResultPair> pairs,
            CancellationToken cancellationToken = default)
        {
            var rows = pairs.Select(p => new GeometriaNaprawiona
            {
                Wariant = variantCode,
                Warstwa = p.IdA,
                IdObiektu = p.IdB,
                PoleM2 = p.Measure ?? 0
            }).ToList();

            await using var transaction = context.Database.CurrentTransaction == null
                ? await context.Database.BeginTransactionAsync(cancellationToken)
                : null;
            await context.GeometrieNaprawione.Where(x => x.Wariant == variantCode).ExecuteDeleteAsync(cancellationToken);
            if (rows.Count > 0)
                await context.BulkInsertAsync(BulkTable<GeometriaNaprawiona>.FromModel(context), rows, cancellationToken);
            if (transaction != null)
                await transaction.CommitAsync(cancellationToken);
        }

        /// <summary>Stan jednego przebiegu NTS: geometrie trzech warstw w pamięci, walidacja i naprawa w fazie obliczenia.</summary>
        private sealed class Run : INtsScenarioRun
        {
            private readonly List<(string Layer, string Id, Geometry Geometry)> _features = [];

            public async Task LoadAsync(ISpatialStore store, CancellationToken cancellationToken)
            {
                foreach (var layer in CheckedLayers)
                {
                    await foreach (var feature in store.ReadFeaturesAsync(layer, cancellationToken))
                        _features.Add((layer.Table, feature.Id, feature.Geometry));
                }
            }

            public bool HasPreparation => false;

            public void Prepare(CancellationToken cancellationToken)
            {
            }

            public bool HasIndex => false;

            public void BuildIndex()
            {
            }

            public IReadOnlyList<ResultPair> Compute(bool parallel, CancellationToken cancellationToken)
            {
                if (!parallel)
                {
                    var pairs = new List<ResultPair>();
                    for (var i = 0; i < _features.Count; i++)
                    {
                        if ((i & 1023) == 0)
                            cancellationToken.ThrowIfCancellationRequested();
                        Check(i, pairs);
                    }
                    return pairs;
                }

                var parts = new ConcurrentBag<List<ResultPair>>();
                Parallel.For(0, _features.Count, new ParallelOptions { CancellationToken = cancellationToken },
                    () => new List<ResultPair>(),
                    (i, _, local) =>
                    {
                        Check(i, local);
                        return local;
                    },
                    local => parts.Add(local));
                return parts.SelectMany(p => p).ToList();
            }

            private void Check(int i, List<ResultPair> pairs)
            {
                var (layer, id, geometry) = _features[i];
                if (geometry.IsValid)
                    return;
                pairs.Add(new ResultPair(layer, id, GeometryFixer.Fix(geometry).Area));
            }
        }
    }
}
