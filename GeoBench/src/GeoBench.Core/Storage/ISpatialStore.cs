using GeoBench.Core.Benchmark;
using GeoBench.Core.Data;
using NetTopologySuite.Geometries;

namespace GeoBench.Core.Storage
{
    /// <summary>Tabela z geometrią czytana przez benchmark: nazwa, kolumna identyfikatora biznesowego i geometrii.</summary>
    public sealed record SpatialLayer(string Name, string Table, string IdColumn, string GeometryColumn)
    {
        public static readonly SpatialLayer Budynki = new("budynki", "budynek", "id_budynku", "geom");
        public static readonly SpatialLayer Dzialki = new("działki", "dzialka", "id_dzialki", "geom");
        public static readonly SpatialLayer Strefy = new("strefy planistyczne", "strefa_planistyczna", "oznaczenie", "geom");
    }

    /// <summary>Obiekt odczytany z magazynu: identyfikator biznesowy i geometria w EPSG:2180.</summary>
    public readonly record struct SpatialFeature(string Id, Geometry Geometry);

    /// <summary>
    /// Wynik przebiegu diagnostycznego (poza pomiarem): plan zapytania w natywnym formacie silnika
    /// (XML showplan albo JSON z EXPLAIN) i komunikaty serwera (np. STATISTICS TIME).
    /// </summary>
    public sealed record QueryDiagnostics(string PlanFormat, string Plan, string Messages);

    /// <summary>
    /// Magazyn danych wariantu: dostęp do tabel przestrzennych z pominięciem śledzenia EF.
    /// Działa na podanym kontekście, także w jego otwartej transakcji (testy integracyjne).
    /// </summary>
    public interface ISpatialStore
    {
        DatabaseEngine Engine { get; }

        /// <summary>Kontekst EF, na którym działa magazyn (zapis tabel wyników).</summary>
        GeoBenchContext Context { get; }

        /// <summary>Liczba wierszy z niepustą geometrią (kontrola wstępna równych danych w obu bazach).</summary>
        Task<long> CountAsync(SpatialLayer layer, CancellationToken cancellationToken = default);

        /// <summary>Identyfikatory obiektów z geometrią niepoprawną według walidatora silnika (poza pomiarem).</summary>
        Task<IReadOnlyList<string>> InvalidIdsAsync(SpatialLayer layer, CancellationToken cancellationToken = default);

        /// <summary>Strumieniowy odczyt identyfikatorów i geometrii w natywnym formacie silnika (bez EF).</summary>
        IAsyncEnumerable<SpatialFeature> ReadFeaturesAsync(SpatialLayer layer, CancellationToken cancellationToken = default);

        /// <summary>Wykonuje zapytanie scenariusza i czyta wszystkie pary (kolumny id_a, id_b i opcjonalnie miara).</summary>
        Task<IReadOnlyList<ResultPair>> ExecuteScenarioAsync(string sql, CancellationToken cancellationToken = default);

        /// <summary>Przebieg diagnostyczny zapytania: rzeczywisty plan i statystyki serwera.</summary>
        Task<QueryDiagnostics> DiagnoseAsync(string sql, CancellationToken cancellationToken = default);

        /// <summary>Ustawienia serwera istotne dla pomiaru (wersja, pamięć, równoległość, parametry indeksów).</summary>
        Task<IReadOnlyList<KeyValuePair<string, string>>> GetServerInfoAsync(CancellationToken cancellationToken = default);
    }

    public static class SpatialStore
    {
        public static ISpatialStore Create(GeoBenchContext context) => context switch
        {
            SqlServerContext sqlServer => new SqlServerSpatialStore(sqlServer),
            PostgresContext postgres => new PostgresSpatialStore(postgres),
            _ => throw new ArgumentException($"Nieobsługiwany kontekst {context.GetType().Name}.", nameof(context))
        };
    }
}
