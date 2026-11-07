using GeoBench.Core.Configuration;
using GeoBench.Core.Data;
using GeoBench.Core.Model;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite;
using NetTopologySuite.Geometries;

namespace GeoBench.Tests.Integration
{
    /// <summary>
    /// Test dymny etapu 1: zapis i odczyt geometrii w obu bazach przez EF Core.
    /// Wymaga baz z nałożonymi migracjami i appsettings.json z connection stringami.
    /// Każdy test działa w transakcji wycofywanej na końcu, więc nie zostawia danych.
    /// </summary>
    public abstract class GeometryRoundTripTestsBase
    {
        // Kwadrat 100 x 100 m z otworem 60 x 60 m w okolicy Częstochowy (EPSG:2180, X = wschód)
        private const double X = 508000, Y = 327000;
        private const double ExpectedArea = 100 * 100 - 60 * 60;

        protected abstract GeoBenchContext CreateContext();

        protected static string ConnectionString(string name) =>
            GeoBenchConfiguration.RequireConnectionString(GeoBenchConfiguration.Build(), name);

        [Fact]
        public async Task Geometry_RoundTrip_PreservesShapeAndSrid()
        {
            var factory = NtsGeometryServices.Instance.CreateGeometryFactory(GeoBenchContext.Srid);
            var original = SquareWithHole(factory);
            var id = $"TEST.{Guid.NewGuid():N}";

            await using var context = CreateContext();
            await using var transaction = await context.Database.BeginTransactionAsync();

            context.Dzialki.Add(new Dzialka { IdDzialki = id, Geom = original });
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();

            var stored = await context.Dzialki.AsNoTracking().SingleAsync(d => d.IdDzialki == id);

            Assert.NotNull(stored.Geom);
            Assert.Equal(GeoBenchContext.Srid, stored.Geom.SRID);
            Assert.True(stored.Geom.IsValid);
            Assert.True(stored.Geom.EqualsExact(original), $"Odczytana geometria różni się od zapisanej: {stored.Geom}");
            Assert.Equal(ExpectedArea, stored.Geom.Area, 6);

            // Zapytanie przestrzenne wykonane w bazie: punkt w pierścieniu trafia, punkt w otworze nie
            var inRing = factory.CreatePoint(new Coordinate(X + 10, Y + 10));
            var inHole = factory.CreatePoint(new Coordinate(X + 50, Y + 50));
            Assert.True(await context.Dzialki.AnyAsync(d => d.IdDzialki == id && d.Geom!.Intersects(inRing)));
            Assert.False(await context.Dzialki.AnyAsync(d => d.IdDzialki == id && d.Geom!.Intersects(inHole)));
        }

        // Geometria w innym znanym układzie (PL-2000 strefa 6, układ pliku POG), czyli zapomniana transformacja.
        // SRID 0 nie nadaje się do tego testu: PostGIS nadaje geometrii bez układu SRID kolumny,
        // a w SQL Server blokuje ją CHECK.
        [Fact]
        public async Task Geometry_WithForeignSrid_IsRejected()
        {
            var factory = NtsGeometryServices.Instance.CreateGeometryFactory(2177);

            await using var context = CreateContext();
            await using var transaction = await context.Database.BeginTransactionAsync();

            context.Dzialki.Add(new Dzialka { IdDzialki = $"TEST.{Guid.NewGuid():N}", Geom = SquareWithHole(factory) });

            await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        }

        private static MultiPolygon SquareWithHole(GeometryFactory factory)
        {
            var shell = factory.CreateLinearRing(
            [
                new Coordinate(X, Y), new Coordinate(X + 100, Y), new Coordinate(X + 100, Y + 100),
                new Coordinate(X, Y + 100), new Coordinate(X, Y)
            ]);
            var hole = factory.CreateLinearRing(
            [
                new Coordinate(X + 20, Y + 20), new Coordinate(X + 20, Y + 80), new Coordinate(X + 80, Y + 80),
                new Coordinate(X + 80, Y + 20), new Coordinate(X + 20, Y + 20)
            ]);
            return factory.CreateMultiPolygon([factory.CreatePolygon(shell, [hole])]);
        }
    }

    [Trait("Category", "Integration")]
    public class SqlServerGeometryRoundTripTests : GeometryRoundTripTestsBase
    {
        protected override GeoBenchContext CreateContext() =>
            new SqlServerContext(SqlServerContext.CreateOptions(ConnectionString(GeoBenchConfiguration.SqlServerConnectionName)));
    }

    [Trait("Category", "Integration")]
    public class PostgresGeometryRoundTripTests : GeometryRoundTripTestsBase
    {
        protected override GeoBenchContext CreateContext() =>
            new PostgresContext(PostgresContext.CreateOptions(ConnectionString(GeoBenchConfiguration.PostgresConnectionName)));
    }
}
