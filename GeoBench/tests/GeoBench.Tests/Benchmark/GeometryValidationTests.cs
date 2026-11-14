using GeoBench.Core.Benchmark;
using GeoBench.Core.Benchmark.Scenarios;
using GeoBench.Core.Data;
using GeoBench.Core.Processing;
using GeoBench.Core.Storage;
using NetTopologySuite.Geometries;

namespace GeoBench.Tests.Benchmark
{
    /// <summary>Scenariusz 08 w NTS: tylko niepoprawne geometrie trzech warstw, pole po naprawie, fazy bez indeksu.</summary>
    public class GeometryValidationTests
    {
        /// <summary>Kokardka 4 × 4 (samoprzecięcie w środku): po naprawie dwa trójkąty, razem 8 m².</summary>
        public static MultiPolygon Bowtie(double x, double y)
        {
            var f = BenchmarkFixtures.Factory;
            var ring = new[]
            {
                new Coordinate(x, y), new Coordinate(x + 4, y + 4), new Coordinate(x + 4, y),
                new Coordinate(x, y + 4), new Coordinate(x, y)
            };
            return f.CreateMultiPolygon([f.CreatePolygon(ring)]);
        }

        private static FakeSpatialStore Store() => new(new()
        {
            [SpatialLayer.Budynki] = [new("b_ok", BenchmarkFixtures.Rectangle(0, 0, 5, 5)), new("b_zle", Bowtie(10, 0))],
            [SpatialLayer.Dzialki] = [new("d_ok", BenchmarkFixtures.Rectangle(0, 0, 50, 50)), new("d_zle", Bowtie(100, 0))],
            [SpatialLayer.Strefy] = [.. BenchmarkFixtures.Zones(), new("S_zle", Bowtie(200, 0))]
        });

        private static readonly (string, string, double)[] Expected =
        [
            ("budynek", "b_zle", 8), ("dzialka", "d_zle", 8), ("strefa_planistyczna", "S_zle", 8)
        ];

        private static (string, string, double)[] Sorted(IEnumerable<ResultPair> pairs) =>
            pairs.OrderBy(p => p.IdA, StringComparer.Ordinal).ThenBy(p => p.IdB, StringComparer.Ordinal)
                .Select(p => (p.IdA, p.IdB, Math.Round(p.Measure ?? -1, 6))).ToArray();

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Nts_ReportsOnlyInvalid_WithAreaAfterRepair(bool parallel)
        {
            var run = await new NtsStrategy(parallel).RunAsync(new GeometryValidationScenario(), Store());

            Assert.Equal(Expected, Sorted(run.Pairs));
            Assert.Equal(new[] { NtsStrategy.ReadPhase, NtsStrategy.ComputePhase }, run.Phases.Select(p => p.Phase));
        }

        [Fact]
        public void Scenario_ChecksThreeLayers()
        {
            IBenchmarkScenario scenario = new GeometryValidationScenario();

            Assert.Equal(new[] { SpatialLayer.Budynki, SpatialLayer.Dzialki, SpatialLayer.Strefy }, scenario.Layers);
            Assert.False(scenario.HandlesInvalidGeometries);
        }

        [Fact]
        public void Describe_CountsPerLayer()
        {
            var notes = new GeometryValidationScenario().Describe([new("budynek", "b1", 8), new("dzialka", "d1", 2)], countA: 2);

            Assert.Equal("niepoprawne geometrie: 2 (budynek 1, dzialka 1, strefa_planistyczna 0)", notes[0]);
        }

        [Theory]
        [InlineData(DatabaseEngine.SqlServer, "MakeValid().STArea()")]
        [InlineData(DatabaseEngine.Postgres, "ST_Area(ST_MakeValid(geom))")]
        public void Sql_IsEmbedded_ForAllLayers(DatabaseEngine engine, string repair)
        {
            var sql = ScenarioSql.Load(engine, new GeometryValidationScenario().SqlFileName);

            Assert.Contains(repair, sql);
            Assert.All(GeometryValidationScenario.CheckedLayers, l => Assert.Contains($"FROM {l.Table} WHERE", sql));
        }
    }
}
