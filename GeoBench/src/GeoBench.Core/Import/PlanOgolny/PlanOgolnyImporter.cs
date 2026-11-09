using System.Diagnostics;
using GeoBench.Core.Data;
using GeoBench.Core.Import.Gml;
using GeoBench.Core.Model;
using GeoBench.Core.Spatial;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;

namespace GeoBench.Core.Import.PlanOgolny
{
    /// <summary>
    /// Import pliku planu ogólnego gminy (GML, APP 2.0) do jednej bazy przez EF Core: akt planowania i jego strefy.
    /// Regulacje (OUZ, OZS) i dokumenty formalne są na razie pomijane i liczone w raporcie.
    /// Ponowny import tego samego aktu zastępuje poprzedni (strefy usuwa kaskada klucza obcego).
    /// Indeks przestrzenny stref powstaje po zapisie danych, z zasięgiem aktu jako BOUNDING_BOX.
    /// Import nie jest pomiarem: służy przygotowaniu danych do scenariuszy.
    /// </summary>
    public sealed class PlanOgolnyImporter
    {
        private const int ProgressInterval = 100;

        public async Task<ImportReport> ImportFileAsync(GeoBenchContext context, string path, bool daneBadawcze,
            IProgress<ImportProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            await using var stream = File.OpenRead(path);
            return await ImportAsync(context, stream, daneBadawcze, progress, cancellationToken);
        }

        /// <exception cref="InvalidDataException">Plik nie zawiera dokładnie jednego poprawnego aktu planowania.</exception>
        public async Task<ImportReport> ImportAsync(GeoBenchContext context, Stream stream, bool daneBadawcze,
            IProgress<ImportProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            var stopwatch = Stopwatch.StartNew();
            var report = new ImportReport();

            // Kolejność obiektów w pliku jest dowolna, więc strefy czekają na akt (2 tys. obiektów mieści się w pamięci)
            MappedFeature<AktPlanowania>? mappedAkt = null;
            var strefy = new List<(MappedFeature<StrefaPlanistyczna> Mapped, string? Plan)>();

            foreach (var record in new GmlFeatureReader().Read(stream))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (++report.Read % ProgressInterval == 0)
                    progress?.Report(new ImportProgress("Odczyt", report.Read));

                try
                {
                    switch (record.TypeName)
                    {
                        case PlanOgolnyMapper.AktTypeName:
                            if (mappedAkt != null)
                                throw new InvalidDataException("Plik zawiera więcej niż jeden akt planowania.");
                            mappedAkt = PlanOgolnyMapper.MapAkt(record, daneBadawcze);
                            break;
                        case PlanOgolnyMapper.StrefaTypeName:
                            strefy.Add((PlanOgolnyMapper.MapStrefa(record), PlanOgolnyMapper.PlanReference(record)));
                            break;
                        default:
                            report.Skip(record.TypeName);
                            break;
                    }
                }
                catch (FeatureMappingException ex)
                {
                    report.Reject(record.TypeName, PlanOgolnyMapper.Identifier(record), ex.Message);
                }
            }

            if (mappedAkt == null)
            {
                var reason = report.Rejected.FirstOrDefault(r => r.TypeName == PlanOgolnyMapper.AktTypeName)?.Reason;
                throw new InvalidDataException("Plik nie zawiera poprawnego aktu planowania." + (reason == null ? "" : $" {reason}"));
            }

            var akt = mappedAkt.Entity;
            Accept(report, PlanOgolnyMapper.AktTypeName, akt.LokalnyId, mappedAkt);
            AttachStrefy(report, akt, strefy);

            progress?.Report(new ImportProgress("Zapis", report.Read));
            await SaveAsync(context, akt, cancellationToken);
            report.Saved = 1 + akt.Strefy.Count;

            var extent = Extent(akt);
            if (extent.IsNull || extent.Width <= 0 || extent.Height <= 0)
            {
                report.Warn(PlanOgolnyMapper.AktTypeName, akt.LokalnyId, "Nie utworzono indeksu przestrzennego: brak geometrii stref.");
            }
            else
            {
                progress?.Report(new ImportProgress("Indeks", report.Saved));
                await context.CreateSpatialIndexAsync<StrefaPlanistyczna>(nameof(StrefaPlanistyczna.Geom), extent, cancellationToken);
            }

            report.Duration = stopwatch.Elapsed;
            return report;
        }

        /// <summary>Kontrola na poziomie importu: przynależność do aktu i unikalność oznaczenia (drugą pilnuje też baza).</summary>
        private static void AttachStrefy(ImportReport report, AktPlanowania akt,
            List<(MappedFeature<StrefaPlanistyczna> Mapped, string? Plan)> strefy)
        {
            var aktReference = $"/{akt.PrzestrzenNazw}/{akt.LokalnyId}";
            var oznaczenia = new HashSet<string>(StringComparer.Ordinal);

            foreach (var (mapped, plan) in strefy)
            {
                var strefa = mapped.Entity;
                if (plan != null && !plan.EndsWith(aktReference, StringComparison.Ordinal) && !plan.Contains(aktReference + "/", StringComparison.Ordinal))
                {
                    report.Reject(PlanOgolnyMapper.StrefaTypeName, strefa.LokalnyId, $"Strefa należy do innego aktu: {plan}.");
                    continue;
                }
                if (!oznaczenia.Add(strefa.Oznaczenie))
                {
                    report.Reject(PlanOgolnyMapper.StrefaTypeName, strefa.LokalnyId, $"Powtórzone oznaczenie strefy: {strefa.Oznaczenie}.");
                    continue;
                }

                Accept(report, PlanOgolnyMapper.StrefaTypeName, strefa.LokalnyId, mapped);
                akt.Strefy.Add(strefa);
            }
        }

        private static void Accept<T>(ImportReport report, string typeName, string id, MappedFeature<T> mapped) where T : class
        {
            report.CountGeometry(mapped.GeometryStatus);
            report.AddArcs(mapped.ArcCount);
            if (mapped.GeometryStatus == GeometryStatus.Repaired)
                report.Warn(typeName, id, $"Geometria naprawiona: {mapped.GeometryReason}");
            else if (mapped.GeometryStatus == GeometryStatus.Rejected)
                report.Warn(typeName, id, $"Zapisano bez geometrii: {mapped.GeometryReason}");
        }

        private static async Task SaveAsync(GeoBenchContext context, AktPlanowania akt, CancellationToken cancellationToken)
        {
            var przestrzenNazw = akt.PrzestrzenNazw;
            var lokalnyId = akt.LokalnyId;

            // Transakcja otwarta przez wywołującego (np. test wycofujący zmiany) obejmuje też indeks: DDL w obu
            // bazach jest transakcyjny. Bez niej import otwiera własną transakcję na usunięcie i zapis.
            await using var transaction = context.Database.CurrentTransaction == null
                ? await context.Database.BeginTransactionAsync(cancellationToken)
                : null;
            await context.AktyPlanowania
                .Where(a => a.PrzestrzenNazw == przestrzenNazw && a.LokalnyId == lokalnyId)
                .ExecuteDeleteAsync(cancellationToken);
            context.AktyPlanowania.Add(akt);
            await context.SaveChangesAsync(cancellationToken);
            if (transaction != null)
                await transaction.CommitAsync(cancellationToken);
        }

        /// <summary>Zasięg aktu powiększony o strefy (mogą nieznacznie wystawać poza granicę gminy).</summary>
        private static Envelope Extent(AktPlanowania akt)
        {
            var extent = akt.Zasieg == null ? new Envelope() : new Envelope(akt.Zasieg.EnvelopeInternal);
            foreach (var strefa in akt.Strefy)
            {
                if (strefa.Geom != null)
                    extent.ExpandToInclude(strefa.Geom.EnvelopeInternal);
            }
            return extent;
        }
    }
}
