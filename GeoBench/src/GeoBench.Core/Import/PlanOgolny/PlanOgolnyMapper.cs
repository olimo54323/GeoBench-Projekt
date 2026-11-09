using System.Globalization;
using GeoBench.Core.Data;
using GeoBench.Core.Model;
using GeoBench.Core.Spatial;
using NetTopologySuite.Geometries;

namespace GeoBench.Core.Import.PlanOgolny
{
    /// <summary>
    /// Zamiana obiektów planu ogólnego gminy (schemat APP 2.0) na encje. Kody list kodowych pochodzą
    /// z końcówki xlink:href (np. …/RodzajStrefyPlanistycznejKod/strefaCmentarzy), liczby i daty są czytane
    /// niezależnie od ustawień regionalnych, a geometria jest przeliczana do EPSG:2180 i normalizowana.
    /// </summary>
    public static class PlanOgolnyMapper
    {
        public const string AktTypeName = "AktPlanowaniaPrzestrzennego";
        public const string StrefaTypeName = "StrefaPlanistyczna";

        public static MappedFeature<AktPlanowania> MapAkt(FeatureRecord record, bool daneBadawcze)
        {
            var akt = new AktPlanowania
            {
                PrzestrzenNazw = Required(record, "przestrzenNazw"),
                LokalnyId = Required(record, "lokalnyId"),
                Tytul = Required(record, "tytul"),
                TytulAlternatywny = record.GetValue("tytulAlternatywny"),
                Status = Code(record, "status"),
                ObowiazujeOd = Date(record, "obowiazujeOd"),
                DaneBadawcze = daneBadawcze
            };

            var (geometry, status, reason, arcs) = MapGeometry(record, "zasiegPrzestrzenny");
            akt.Zasieg = geometry;
            return new MappedFeature<AktPlanowania>(akt, status, reason, arcs);
        }

        /// <summary>Strefa bez powiązania z aktem; powiązanie ustala importer.</summary>
        public static MappedFeature<StrefaPlanistyczna> MapStrefa(FeatureRecord record)
        {
            var strefa = new StrefaPlanistyczna
            {
                Oznaczenie = Required(record, "oznaczenie"),
                LokalnyId = Required(record, "lokalnyId"),
                Symbol = Required(record, "symbol"),
                Nazwa = Rodzaj(record),
                NazwaAlternatywna = record.GetValue("nazwaAlternatywna"),
                MaksNadziemnaIntensywnoscZabudowy = Number(record, "maksNadziemnaIntensywnoscZabudowy"),
                MaksUdzialPowierzchniZabudowy = Number(record, "maksUdzialPowierzchniZabudowy"),
                MaksWysokoscZabudowy = Number(record, "maksWysokoscZabudowy"),
                MinUdzialPowierzchniBiologicznieCzynnej = Number(record, "minUdzialPowierzchniBiologicznieCzynnej"),
                Status = Code(record, "status"),
                CharakterUstalenia = Code(record, "charakterUstalenia"),
                ObowiazujeOd = Date(record, "obowiazujeOd"),
                PoczatekWersjiObiektu = UtcDateTime(record, "poczatekWersjiObiektu")
            };

            var (geometry, status, reason, arcs) = MapGeometry(record, "geometria");
            strefa.Geom = geometry;
            return new MappedFeature<StrefaPlanistyczna>(strefa, status, reason, arcs);
        }

        /// <summary>Odnośnik obiektu do aktu planowania (pole plan), np. …/AktPlanowaniaPrzestrzennego/PL.ZIPPZP.9398/246401-POG/1POG.</summary>
        public static string? PlanReference(FeatureRecord record) => record.Find("plan")?.Href;

        /// <summary>Identyfikator obiektu do logu: lokalnyId z idIIP, a gdy go brak – gml:id.</summary>
        public static string? Identifier(FeatureRecord record) => record.GetValue("lokalnyId") ?? record.GmlId;

        private static MappedGeometry MapGeometry(FeatureRecord record, string property) => GeometryMapping.Map(record, property);

        private static RodzajStrefyPlanistycznej Rodzaj(FeatureRecord record)
        {
            var code = Code(record, "nazwa") ?? throw new FeatureMappingException("Brak rodzaju strefy (pole nazwa).");
            // Kody listy (strefaCmentarzy) odpowiadają nazwom wartości enuma; liczby nie są kodami
            if (!char.IsDigit(code[0]) && Enum.TryParse<RodzajStrefyPlanistycznej>(code, ignoreCase: true, out var rodzaj)
                && Enum.IsDefined(rodzaj))
                return rodzaj;
            throw new FeatureMappingException($"Nieznany rodzaj strefy: {code}.");
        }

        private static string Required(FeatureRecord record, string name) =>
            record.GetValue(name) ?? throw new FeatureMappingException($"Brak pola {name}.");

        /// <summary>Kod z końcówki xlink:href (po ostatnim / lub #).</summary>
        private static string? Code(FeatureRecord record, string name)
        {
            var href = record.Find(name)?.Href;
            if (string.IsNullOrWhiteSpace(href))
                return null;
            var code = href.TrimEnd('/');
            code = code[(code.LastIndexOfAny(['/', '#']) + 1)..];
            return code.Length == 0 ? null : code;
        }

        private static decimal? Number(FeatureRecord record, string name)
        {
            var value = record.GetValue(name);
            if (value == null)
                return null;
            if (decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
                return number;
            throw new FeatureMappingException($"Pole {name} nie jest liczbą: {value}.");
        }

        private static DateOnly? Date(FeatureRecord record, string name)
        {
            var value = record.GetValue(name);
            if (value == null)
                return null;
            if (DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                return date;
            throw new FeatureMappingException($"Pole {name} nie jest datą: {value}.");
        }

        /// <summary>xs:dateTime jako UTC (timestamptz w PostgreSQL wymaga DateTimeKind.Utc); brak strefy = UTC.</summary>
        private static DateTime? UtcDateTime(FeatureRecord record, string name)
        {
            var value = record.GetValue(name);
            if (value == null)
                return null;
            if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dateTime))
                return dateTime.UtcDateTime;
            throw new FeatureMappingException($"Pole {name} nie jest datą i czasem: {value}.");
        }
    }
}
