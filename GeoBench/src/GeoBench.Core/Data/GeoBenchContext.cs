using System.Globalization;
using GeoBench.Core.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using NetTopologySuite.Geometries;

namespace GeoBench.Core.Data
{
    /// <summary>
    /// Wspólny model danych obu baz. Nazwy tabel i kolumn w snake_case nadaje konwencja
    /// (UseSnakeCaseNamingConvention), typy zależne od silnika podają konteksty pochodne.
    /// Indeksy przestrzenne nie są częścią modelu: powstają po załadowaniu danych.
    /// </summary>
    public abstract class GeoBenchContext : DbContext
    {
        /// <summary>Układ współrzędnych wszystkich geometrii w bazie (PL-1992).</summary>
        public const int Srid = 2180;

        protected GeoBenchContext(DbContextOptions options) : base(options)
        {
        }

        public DbSet<Dzialka> Dzialki => Set<Dzialka>();
        public DbSet<Budynek> Budynki => Set<Budynek>();
        public DbSet<AktPlanowania> AktyPlanowania => Set<AktPlanowania>();
        public DbSet<StrefaPlanistyczna> StrefyPlanistyczne => Set<StrefaPlanistyczna>();

        /// <summary>Typ kolumny geometrii dla danego typu OGC (np. MultiPolygon).</summary>
        protected abstract string GeometryColumnType(string geometryType);

        /// <summary>Warunek CHECK sprawdzający SRID kolumny geometrii.</summary>
        protected abstract string SridCheckSql(string column);

        /// <summary>Typ kolumny dla daty i czasu bez strefy (np. pole DATA z WFS).</summary>
        protected abstract string LocalDateTimeColumnType { get; }

        /// <summary>Zapytanie zwracające jeden tekst z wersją serwera (i rozszerzenia przestrzennego).</summary>
        protected abstract string ServerVersionSql { get; }

        /// <summary>
        /// Polecenia tworzące od nowa indeks przestrzenny kolumny i odświeżające statystyki tabeli.
        /// Zasięg danych jest potrzebny indeksowi siatkowemu SQL Server (BOUNDING_BOX).
        /// </summary>
        protected abstract string SpatialIndexSql(string table, string column, Envelope extent);

        /// <summary>Wersja serwera bazy, np. do zakładki Połączenia i do raportu z pomiaru.</summary>
        public async Task<string?> GetServerVersionAsync(CancellationToken cancellationToken = default)
        {
            await Database.OpenConnectionAsync(cancellationToken);
            try
            {
                await using var command = Database.GetDbConnection().CreateCommand();
                command.CommandText = ServerVersionSql;
                return Convert.ToString(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
            }
            finally
            {
                await Database.CloseConnectionAsync();
            }
        }

        /// <summary>
        /// Tworzy (lub tworzy ponownie) indeks przestrzenny kolumny geometrii encji, po załadowaniu danych.
        /// Nazwy tabeli i kolumny pochodzą z modelu, więc obowiązuje ta sama konwencja nazw co w migracjach.
        /// </summary>
        public Task CreateSpatialIndexAsync<TEntity>(string propertyName, Envelope extent, CancellationToken cancellationToken = default)
        {
            var entity = Model.FindEntityType(typeof(TEntity))
                ?? throw new InvalidOperationException($"Typ {typeof(TEntity).Name} nie należy do modelu.");
            var table = entity.GetTableName()!;
            var column = entity.FindProperty(propertyName)?.GetColumnName(StoreObjectIdentifier.Table(table, entity.GetSchema()))
                ?? throw new ArgumentException($"Encja {typeof(TEntity).Name} nie ma właściwości {propertyName}.", nameof(propertyName));
            if (extent.IsNull || extent.Width <= 0 || extent.Height <= 0)
                throw new ArgumentException("Zasięg indeksu przestrzennego musi mieć niezerową szerokość i wysokość.", nameof(extent));

            return Database.ExecuteSqlRawAsync(SpatialIndexSql(table, column, extent), cancellationToken);
        }

        /// <summary>Liczba w SQL niezależnie od ustawień regionalnych (przecinek zepsułby polecenie).</summary>
        protected static string SqlNumber(double value) => value.ToString("R", CultureInfo.InvariantCulture);

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Dzialka>(e =>
            {
                e.ToTable("dzialka", t => t.HasCheckConstraint("ck_dzialka_geom_srid", SridCheckSql("geom")));
                e.HasKey(x => x.Oid);
                // Długości według DescribeFeatureType usługi WFS EGiB
                e.Property(x => x.IdDzialki).HasMaxLength(250);
                e.Property(x => x.NumerDzialki).HasMaxLength(200);
                e.Property(x => x.NumerObrebu).HasMaxLength(200);
                e.Property(x => x.NazwaObrebu).HasMaxLength(200);
                e.Property(x => x.NumerJednostki).HasMaxLength(50);
                e.Property(x => x.NazwaGminy).HasMaxLength(50);
                e.Property(x => x.PoleEwidencyjne).HasPrecision(12, 4);
                e.Property(x => x.KlasouzytkiEgib).HasMaxLength(250);
                e.Property(x => x.GrupaRejestrowa).HasMaxLength(50);
                e.Property(x => x.Data).HasColumnType(LocalDateTimeColumnType);
                e.Property(x => x.Geom).HasColumnType(GeometryColumnType("MultiPolygon"));
                e.HasIndex(x => x.IdDzialki).IsUnique();
            });

            modelBuilder.Entity<Budynek>(e =>
            {
                e.ToTable("budynek", t => t.HasCheckConstraint("ck_budynek_geom_srid", SridCheckSql("geom")));
                e.HasKey(x => x.Oid);
                e.Property(x => x.IdBudynku).HasMaxLength(200);
                e.Property(x => x.Rodzaj).HasMaxLength(64);
                e.Property(x => x.Data).HasColumnType(LocalDateTimeColumnType);
                e.Property(x => x.Geom).HasColumnType(GeometryColumnType("MultiPolygon"));
                e.HasIndex(x => x.IdBudynku).IsUnique();
            });

            modelBuilder.Entity<AktPlanowania>(e =>
            {
                e.ToTable("akt_planowania", t => t.HasCheckConstraint("ck_akt_planowania_zasieg_srid", SridCheckSql("zasieg")));
                e.HasKey(x => x.Oid);
                e.Property(x => x.PrzestrzenNazw).HasMaxLength(255);
                e.Property(x => x.LokalnyId).HasMaxLength(255);
                e.Property(x => x.Tytul).HasMaxLength(1000);
                e.Property(x => x.TytulAlternatywny).HasMaxLength(1000);
                e.Property(x => x.Status).HasMaxLength(100);
                e.Property(x => x.Zasieg).HasColumnType(GeometryColumnType("MultiPolygon"));
                e.HasIndex(x => new { x.PrzestrzenNazw, x.LokalnyId }).IsUnique();
            });

            modelBuilder.Entity<StrefaPlanistyczna>(e =>
            {
                e.ToTable("strefa_planistyczna", t => t.HasCheckConstraint("ck_strefa_planistyczna_geom_srid", SridCheckSql("geom")));
                e.HasKey(x => x.Oid);
                e.Property(x => x.Oznaczenie).HasMaxLength(100);
                e.Property(x => x.LokalnyId).HasMaxLength(255);
                e.Property(x => x.Symbol).HasMaxLength(10);
                e.Property(x => x.Nazwa).HasConversion<string>().HasMaxLength(100);
                e.Property(x => x.NazwaAlternatywna).HasMaxLength(1000);
                e.Property(x => x.MaksNadziemnaIntensywnoscZabudowy).HasPrecision(8, 2);
                e.Property(x => x.MaksUdzialPowierzchniZabudowy).HasPrecision(5, 2);
                e.Property(x => x.MaksWysokoscZabudowy).HasPrecision(8, 2);
                e.Property(x => x.MinUdzialPowierzchniBiologicznieCzynnej).HasPrecision(5, 2);
                e.Property(x => x.Status).HasMaxLength(100);
                e.Property(x => x.CharakterUstalenia).HasMaxLength(100);
                e.Property(x => x.Geom).HasColumnType(GeometryColumnType("MultiPolygon"));
                e.HasOne(x => x.AktPlanowania).WithMany(a => a.Strefy).HasForeignKey(x => x.AktPlanowaniaOid);
                e.HasIndex(x => new { x.AktPlanowaniaOid, x.Oznaczenie }).IsUnique();
            });
        }
    }
}
