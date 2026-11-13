using GeoBench.Core.Data;
using GeoBench.Core.Storage;

namespace GeoBench.Core.Benchmark
{
    /// <summary>
    /// Scenariusz testowy: to samo zadanie przestrzenne w dwóch postaciach – zapytanie SQL (strategia w bazie)
    /// i obliczenie w NTS na danych odczytanych z magazynu (strategia w C#). Wynik obu to zbiór par.
    /// </summary>
    public interface IBenchmarkScenario
    {
        /// <summary>Numer scenariusza (np. 04), także prefiks pliku SQL.</summary>
        string Id { get; }

        string Name { get; }

        /// <summary>Plik zapytania w sql/&lt;silnik&gt;/scenarios (zasób osadzony).</summary>
        string SqlFileName { get; }

        /// <summary>Warstwa A (lewa strona par), np. budynki; jej liczność służy diagnostyce wyniku.</summary>
        SpatialLayer LayerA { get; }

        /// <summary>Warstwa B (prawa strona par), np. strefy planistyczne.</summary>
        SpatialLayer LayerB { get; }

        /// <summary>Czy pary mają miarę (np. pole przecięcia), sumowaną w odcisku wyniku.</summary>
        bool HasMeasure { get; }

        /// <summary>
        /// Czy scenariusz obsługuje niepoprawne geometrie (naprawa albo odrzucenie); wtedy kontrola wstępna
        /// liczy je w każdej bazie i raport podaje ich identyfikatory.
        /// </summary>
        bool HandlesInvalidGeometries { get; }

        /// <summary>Ustawienia serii przed pierwszym przebiegiem (np. wybór zapytania z naprawą geometrii).</summary>
        void Configure(BenchmarkOptions options);

        /// <summary>Nowy, pusty przebieg NTS (stan jednego pomiaru: dane, indeks).</summary>
        INtsScenarioRun CreateNtsRun();

        /// <summary>Opis wyniku do raportu, np. liczba budynków bez strefy i w kilku strefach.</summary>
        IReadOnlyList<string> Describe(IReadOnlyList<ResultPair> pairs, long countA);

        /// <summary>
        /// Zastępuje wiersze wariantu w tabeli wyników scenariusza (np. budynki_stref) zapisem masowym.
        /// Poza pomiarem czasu, po walidacji.
        /// </summary>
        Task SaveResultsAsync(GeoBenchContext context, string variantCode, IReadOnlyList<ResultPair> pairs,
            CancellationToken cancellationToken = default);
    }

    /// <summary>Przebieg scenariusza w NTS podzielony na fazy mierzone osobno.</summary>
    public interface INtsScenarioRun
    {
        /// <summary>Faza odczytu: identyfikatory i geometrie z magazynu do pamięci.</summary>
        Task LoadAsync(ISpatialStore store, CancellationToken cancellationToken);

        /// <summary>Czy przebieg ma fazę przygotowania (np. kontrola i naprawa geometrii).</summary>
        bool HasPreparation { get; }

        /// <summary>Faza przygotowania danych po odczycie, mierzona osobno.</summary>
        void Prepare(CancellationToken cancellationToken);

        /// <summary>Faza indeksu: STRtree i geometrie przygotowane (PreparedGeometry).</summary>
        void BuildIndex();

        /// <summary>Faza obliczenia: pary wyniku, jednowątkowo albo przez Parallel.For.</summary>
        IReadOnlyList<ResultPair> Compute(bool parallel, CancellationToken cancellationToken);
    }
}
