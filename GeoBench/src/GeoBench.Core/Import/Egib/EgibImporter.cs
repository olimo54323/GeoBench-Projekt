using System.Diagnostics;
using GeoBench.Core.Data;
using GeoBench.Core.Data.Bulk;
using GeoBench.Core.Import.Wfs;
using GeoBench.Core.Model;
using NetTopologySuite.Geometries;

namespace GeoBench.Core.Import.Egib
{
    /// <summary>
    /// Faza B importu EGiB: obiekty (zwykle z cache stron WFS) do jednej bazy przez zapis masowy.
    /// Tabela jest czyszczona i ładowana od nowa w jednej transakcji; indeks przestrzenny jest usuwany
    /// przed ładowaniem i budowany po nim. Duplikaty identyfikatorów są pomijane (pierwszy wygrywa),
    /// geometria nie do naprawienia daje obiekt bez geometrii. Import przygotowuje dane i nie jest pomiarem.
    /// </summary>
    public sealed class EgibImporter
    {
        public const int BatchSize = 5000;
        private const int ProgressInterval = 1000;

        public Task<ImportReport> ImportAsync(GeoBenchContext context, IEnumerable<FeatureRecord> records, WfsLayerOptions layer,
            IProgress<ImportProgress>? progress = null, CancellationToken cancellationToken = default) =>
            layer.Target switch
            {
                EgibTarget.Dzialka => ImportAsync(context, records, layer.LocalTypeName, EgibMapper.MapDzialka,
                    d => d.IdDzialki, d => d.Geom, nameof(Dzialka.Geom), progress, cancellationToken),
                EgibTarget.Budynek => ImportAsync(context, records, layer.LocalTypeName, EgibMapper.MapBudynek,
                    b => b.IdBudynku, b => b.Geom, nameof(Budynek.Geom), progress, cancellationToken),
                _ => throw new ArgumentOutOfRangeException(nameof(layer), layer.Target, null)
            };

        private static async Task<ImportReport> ImportAsync<T>(GeoBenchContext context, IEnumerable<FeatureRecord> records,
            string typeName, Func<FeatureRecord, MappedFeature<T>> map, Func<T, string> key, Func<T, Geometry?> geometry,
            string geometryProperty, IProgress<ImportProgress>? progress, CancellationToken cancellationToken) where T : class
        {
            var stopwatch = Stopwatch.StartNew();
            var report = new ImportReport();
            var table = BulkTable<T>.FromModel(context);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var batch = new List<T>(BatchSize);
            var extent = new Envelope();
            var stage = $"Zapis {typeName}";

            // Transakcja wywołującego (np. test wycofujący zmiany) obejmuje cały import; inaczej własna
            await using var transaction = context.Database.CurrentTransaction == null
                ? await context.Database.BeginTransactionAsync(cancellationToken)
                : null;

            await context.DropSpatialIndexAsync<T>(geometryProperty, cancellationToken);
            await context.TruncateAsync<T>(cancellationToken);

            foreach (var record in records)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (++report.Read % ProgressInterval == 0)
                    progress?.Report(new ImportProgress(stage, report.Read));

                if (!string.Equals(record.TypeName, typeName, StringComparison.OrdinalIgnoreCase))
                {
                    report.Skip(record.TypeName);
                    continue;
                }

                MappedFeature<T> mapped;
                try
                {
                    mapped = map(record);
                }
                catch (FeatureMappingException ex)
                {
                    report.Reject(record.TypeName, EgibMapper.Identifier(record), ex.Message);
                    continue;
                }

                var id = key(mapped.Entity);
                if (!seen.Add(id))
                {
                    report.Duplicates++;
                    continue;
                }

                report.Accept(typeName, id, mapped);
                if (geometry(mapped.Entity) is { } g)
                    extent.ExpandToInclude(g.EnvelopeInternal);

                batch.Add(mapped.Entity);
                if (batch.Count == BatchSize)
                    await FlushAsync(context, table, batch, report, cancellationToken);
            }
            await FlushAsync(context, table, batch, report, cancellationToken);

            if (extent.IsNull || extent.Width <= 0 || extent.Height <= 0)
            {
                report.Warn(typeName, null, "Nie utworzono indeksu przestrzennego: brak geometrii.");
            }
            else
            {
                progress?.Report(new ImportProgress($"Indeks {typeName}", report.Saved));
                await context.CreateSpatialIndexAsync<T>(geometryProperty, extent, cancellationToken);
            }

            if (transaction != null)
                await transaction.CommitAsync(cancellationToken);

            report.Duration = stopwatch.Elapsed;
            return report;
        }

        private static async Task FlushAsync<T>(GeoBenchContext context, BulkTable<T> table, List<T> batch, ImportReport report,
            CancellationToken cancellationToken) where T : class
        {
            if (batch.Count == 0)
                return;
            await context.BulkInsertAsync(table, batch, cancellationToken);
            report.Saved += batch.Count;
            batch.Clear();
        }
    }
}
