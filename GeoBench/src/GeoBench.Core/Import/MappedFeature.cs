using GeoBench.Core.Spatial;

namespace GeoBench.Core.Import
{
    /// <summary>
    /// Encja utworzona z obiektu źródła wraz z wynikiem przygotowania geometrii.
    /// Geometria odrzucona nie odrzuca obiektu: encja ma wtedy pustą geometrię, a powód trafia do raportu.
    /// </summary>
    public sealed record MappedFeature<T>(T Entity, GeometryStatus GeometryStatus, string? GeometryReason, int ArcCount)
        where T : class;

    /// <summary>Obiektu źródła nie da się zamienić na encję (brak wymaganego pola, zła wartość).</summary>
    public sealed class FeatureMappingException(string message) : Exception(message);
}
