using GeoBench.Core.Spatial;

namespace GeoBench.Core.Import
{
    /// <summary>
    /// Postęp importu dla IProgress&lt;T&gt;: etap i liczba przetworzonych elementów (obiektów albo stron);
    /// Total, gdy liczba wszystkich jest znana (np. stron WFS).
    /// </summary>
    public sealed record ImportProgress(string Stage, int Processed, int? Total = null);

    /// <summary>Obiekt, którego nie zapisano, z powodem (log odrzuconych).</summary>
    public sealed record RejectedFeature(string TypeName, string? Id, string Reason);

    /// <summary>Uwaga do obiektu zapisanego z ograniczeniem, np. bez geometrii.</summary>
    public sealed record ImportWarning(string TypeName, string? Id, string Message);

    /// <summary>
    /// Podsumowanie importu jednego źródła do jednej bazy. Liczniki geometrii dotyczą obiektów zapisanych:
    /// geometria odrzucona oznacza zapis obiektu bez geometrii (z ostrzeżeniem).
    /// </summary>
    public sealed class ImportReport
    {
        private readonly List<RejectedFeature> _rejected = [];
        private readonly List<ImportWarning> _warnings = [];
        private readonly Dictionary<string, int> _skippedByType = new(StringComparer.Ordinal);

        /// <summary>Wszystkie obiekty odczytane ze źródła.</summary>
        public int Read { get; set; }

        public int Saved { get; set; }

        /// <summary>Obiekty o identyfikatorze już zapisanym w tym imporcie (np. na styku stron WFS); pomijane.</summary>
        public int Duplicates { get; set; }

        public int GeometryValid { get; private set; }
        public int GeometryRepaired { get; private set; }
        public int GeometryRejected { get; private set; }

        /// <summary>Łuki zamienione na odcinki (ArcLinearizer).</summary>
        public int Arcs { get; private set; }

        public TimeSpan Duration { get; set; }

        public IReadOnlyList<RejectedFeature> Rejected => _rejected;
        public IReadOnlyList<ImportWarning> Warnings => _warnings;

        /// <summary>Obiekty typów, których model jeszcze nie obsługuje (np. regulacje POG), według typu.</summary>
        public IReadOnlyDictionary<string, int> SkippedByType => _skippedByType;

        public int Skipped => _skippedByType.Values.Sum();

        public void Reject(string typeName, string? id, string reason) => _rejected.Add(new RejectedFeature(typeName, id, reason));

        public void Warn(string typeName, string? id, string message) => _warnings.Add(new ImportWarning(typeName, id, message));

        public void Skip(string typeName) => _skippedByType[typeName] = _skippedByType.GetValueOrDefault(typeName) + 1;

        public void AddArcs(int count) => Arcs += count;

        public void CountGeometry(GeometryStatus status)
        {
            switch (status)
            {
                case GeometryStatus.Valid: GeometryValid++; break;
                case GeometryStatus.Repaired: GeometryRepaired++; break;
                case GeometryStatus.Rejected: GeometryRejected++; break;
            }
        }

        /// <summary>Liczy geometrię przyjętego obiektu; naprawa i brak geometrii trafiają do ostrzeżeń.</summary>
        public void Accept<T>(string typeName, string? id, MappedFeature<T> mapped) where T : class
        {
            CountGeometry(mapped.GeometryStatus);
            AddArcs(mapped.ArcCount);
            if (mapped.GeometryStatus == GeometryStatus.Repaired)
                Warn(typeName, id, $"Geometria naprawiona: {mapped.GeometryReason}");
            else if (mapped.GeometryStatus == GeometryStatus.Rejected)
                Warn(typeName, id, $"Zapisano bez geometrii: {mapped.GeometryReason}");
        }

        public override string ToString() =>
            $"odczytane {Read}, zapisane {Saved}, odrzucone {Rejected.Count}, pominięte {Skipped}" +
            (Duplicates > 0 ? $", duplikaty {Duplicates}" : "") + "; " +
            $"geometrie: poprawne {GeometryValid}, naprawione {GeometryRepaired}, odrzucone {GeometryRejected}, łuki {Arcs}; " +
            $"czas {Duration.TotalSeconds:F1} s";
    }
}
