using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NetTopologySuite.Geometries;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace GeoBench.Core.Data.Migrations.Postgres
{
    /// <inheritdoc />
    public partial class Init : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:postgis", ",,");

            migrationBuilder.CreateTable(
                name: "akt_planowania",
                columns: table => new
                {
                    oid = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    przestrzen_nazw = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    lokalny_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    tytul = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    tytul_alternatywny = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    status = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    obowiazuje_od = table.Column<DateOnly>(type: "date", nullable: true),
                    dane_badawcze = table.Column<bool>(type: "boolean", nullable: false),
                    zasieg = table.Column<MultiPolygon>(type: "geometry(MultiPolygon,2180)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_akt_planowania", x => x.oid);
                    table.CheckConstraint("ck_akt_planowania_zasieg_srid", "ST_SRID(zasieg) = 2180");
                });

            migrationBuilder.CreateTable(
                name: "budynek",
                columns: table => new
                {
                    oid = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    id_budynku = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    rodzaj = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    kondygnacje_nadziemne = table.Column<int>(type: "integer", nullable: true),
                    kondygnacje_podziemne = table.Column<int>(type: "integer", nullable: true),
                    data = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    geom = table.Column<MultiPolygon>(type: "geometry(MultiPolygon,2180)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_budynek", x => x.oid);
                    table.CheckConstraint("ck_budynek_geom_srid", "ST_SRID(geom) = 2180");
                });

            migrationBuilder.CreateTable(
                name: "dzialka",
                columns: table => new
                {
                    oid = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    id_dzialki = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    numer_dzialki = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    numer_obrebu = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    nazwa_obrebu = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    numer_jednostki = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    nazwa_gminy = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    pole_ewidencyjne = table.Column<decimal>(type: "numeric(12,4)", precision: 12, scale: 4, nullable: true),
                    klasouzytki_egib = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    grupa_rejestrowa = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    data = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    geom = table.Column<MultiPolygon>(type: "geometry(MultiPolygon,2180)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_dzialka", x => x.oid);
                    table.CheckConstraint("ck_dzialka_geom_srid", "ST_SRID(geom) = 2180");
                });

            migrationBuilder.CreateTable(
                name: "strefa_planistyczna",
                columns: table => new
                {
                    oid = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    akt_planowania_oid = table.Column<int>(type: "integer", nullable: false),
                    oznaczenie = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    lokalny_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    symbol = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    nazwa = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    nazwa_alternatywna = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    maks_nadziemna_intensywnosc_zabudowy = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: true),
                    maks_udzial_powierzchni_zabudowy = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    maks_wysokosc_zabudowy = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: true),
                    min_udzial_powierzchni_biologicznie_czynnej = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    status = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    charakter_ustalenia = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    obowiazuje_od = table.Column<DateOnly>(type: "date", nullable: true),
                    poczatek_wersji_obiektu = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    geom = table.Column<MultiPolygon>(type: "geometry(MultiPolygon,2180)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_strefa_planistyczna", x => x.oid);
                    table.CheckConstraint("ck_strefa_planistyczna_geom_srid", "ST_SRID(geom) = 2180");
                    table.ForeignKey(
                        name: "fk_strefa_planistyczna_akt_planowania_akt_planowania_oid",
                        column: x => x.akt_planowania_oid,
                        principalTable: "akt_planowania",
                        principalColumn: "oid",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_akt_planowania_przestrzen_nazw_lokalny_id",
                table: "akt_planowania",
                columns: new[] { "przestrzen_nazw", "lokalny_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_budynek_id_budynku",
                table: "budynek",
                column: "id_budynku",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_dzialka_id_dzialki",
                table: "dzialka",
                column: "id_dzialki",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_strefa_planistyczna_akt_planowania_oid_oznaczenie",
                table: "strefa_planistyczna",
                columns: new[] { "akt_planowania_oid", "oznaczenie" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "budynek");

            migrationBuilder.DropTable(
                name: "dzialka");

            migrationBuilder.DropTable(
                name: "strefa_planistyczna");

            migrationBuilder.DropTable(
                name: "akt_planowania");
        }
    }
}
