using System.Globalization;
using GeoBench.Core.Model;

namespace GeoBench.Core.Import.Egib
{
    /// <summary>
    /// Zamiana obiektów WFS EGiB (działki, budynki) na encje. Usługi różnią się zapisem pól (wielkość liter,
    /// literówka POLE_EWIDENYJNE w usłudze zbiorczej), więc pola mają aliasy. Liczby i daty są czytane
    /// niezależnie od ustawień regionalnych; geometria przechodzi przez GeometryMapping.
    /// </summary>
    public static class EgibMapper
    {
        public const string GeometryProperty = "geom";

        private static readonly string[] DateFormats =
        [
            "yyyy-MM-dd'T'HH:mm:ss",
            "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF",
            "yyyy-MM-dd HH:mm:ss",
            "yyyy-MM-dd"
        ];

        public static MappedFeature<Dzialka> MapDzialka(FeatureRecord record)
        {
            var dzialka = new Dzialka
            {
                IdDzialki = Required(record, "ID_DZIALKI"),
                NumerDzialki = record.GetValue("NUMER_DZIALKI"),
                NumerObrebu = record.GetValue("NUMER_OBREBU"),
                NazwaObrebu = record.GetValue("NAZWA_OBREBU"),
                NumerJednostki = record.GetValue("NUMER_JEDNOSTKI"),
                NazwaGminy = record.GetValue("NAZWA_GMINY"),
                PoleEwidencyjne = Number(record, "POLE_EWIDENCYJNE", "POLE_EWIDENYJNE"),
                KlasouzytkiEgib = record.GetValue("KLASOUZYTKI_EGIB"),
                GrupaRejestrowa = record.GetValue("GRUPA_REJESTROWA"),
                Data = LocalDateTime(record, "DATA")
            };

            var geometry = GeometryMapping.Map(record, GeometryProperty);
            dzialka.Geom = geometry.Geometry;
            return new MappedFeature<Dzialka>(dzialka, geometry.Status, geometry.Reason, geometry.Arcs);
        }

        public static MappedFeature<Budynek> MapBudynek(FeatureRecord record)
        {
            var budynek = new Budynek
            {
                IdBudynku = Required(record, "ID_BUDYNKU"),
                Rodzaj = record.GetValue("RODZAJ"),
                KondygnacjeNadziemne = Integer(record, "KONDYGNACJE_NADZIEMNE"),
                KondygnacjePodziemne = Integer(record, "KONDYGNACJE_PODZIEMNE"),
                Data = LocalDateTime(record, "DATA")
            };

            var geometry = GeometryMapping.Map(record, GeometryProperty);
            budynek.Geom = geometry.Geometry;
            return new MappedFeature<Budynek>(budynek, geometry.Status, geometry.Reason, geometry.Arcs);
        }

        /// <summary>Identyfikator obiektu do logu, także gdy mapowanie się nie powiodło.</summary>
        public static string? Identifier(FeatureRecord record) =>
            record.GetValue("ID_DZIALKI") ?? record.GetValue("ID_BUDYNKU") ?? record.GmlId;

        private static string Required(FeatureRecord record, string name) =>
            record.GetValue(name) ?? throw new FeatureMappingException($"Brak pola {name}.");

        private static string? FirstValue(FeatureRecord record, string[] names, out string name)
        {
            foreach (var candidate in names)
            {
                var value = record.GetValue(candidate);
                if (value != null)
                {
                    name = candidate;
                    return value;
                }
            }
            name = names[0];
            return null;
        }

        private static decimal? Number(FeatureRecord record, params string[] names)
        {
            var value = FirstValue(record, names, out var name);
            if (value == null)
                return null;
            if (decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
                return number;
            throw new FeatureMappingException($"Pole {name} nie jest liczbą: {value}.");
        }

        private static int? Integer(FeatureRecord record, string name)
        {
            var value = record.GetValue(name);
            if (value == null)
                return null;
            // Część usług podaje liczbę kondygnacji jako 2.0
            if (decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
                && number == decimal.Truncate(number) && number is >= int.MinValue and <= int.MaxValue)
                return (int)number;
            throw new FeatureMappingException($"Pole {name} nie jest liczbą całkowitą: {value}.");
        }

        /// <summary>
        /// Data z usługi jako czas lokalny bez strefy (kolumna timestamp without time zone / datetime2).
        /// Gdy wartość ma strefę, zostaje jej zegar (bez przeliczania), żeby obie bazy dostały to samo.
        /// </summary>
        private static DateTime? LocalDateTime(FeatureRecord record, string name)
        {
            var value = record.GetValue(name);
            if (value == null)
                return null;
            if (DateTime.TryParseExact(value, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                return DateTime.SpecifyKind(date, DateTimeKind.Unspecified);
            if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var withOffset))
                return DateTime.SpecifyKind(withOffset.DateTime, DateTimeKind.Unspecified);
            throw new FeatureMappingException($"Pole {name} nie jest datą: {value}.");
        }
    }
}
