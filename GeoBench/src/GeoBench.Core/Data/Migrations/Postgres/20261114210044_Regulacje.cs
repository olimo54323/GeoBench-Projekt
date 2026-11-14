using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NetTopologySuite.Geometries;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace GeoBench.Core.Data.Migrations.Postgres
{
    /// <inheritdoc />
    public partial class Regulacje : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "regulacja",
                columns: table => new
                {
                    oid = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    akt_planowania_oid = table.Column<int>(type: "integer", nullable: false),
                    rodzaj = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    oznaczenie = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    lokalny_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    symbol = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    nazwa = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    status = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    charakter_ustalenia = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    obowiazuje_od = table.Column<DateOnly>(type: "date", nullable: true),
                    poczatek_wersji_obiektu = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    geom = table.Column<MultiPolygon>(type: "geometry(MultiPolygon,2180)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_regulacja", x => x.oid);
                    table.CheckConstraint("ck_regulacja_geom_srid", "ST_SRID(geom) = 2180");
                    table.ForeignKey(
                        name: "fk_regulacja_akt_planowania_akt_planowania_oid",
                        column: x => x.akt_planowania_oid,
                        principalTable: "akt_planowania",
                        principalColumn: "oid",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_regulacja_akt_planowania_oid_oznaczenie",
                table: "regulacja",
                columns: new[] { "akt_planowania_oid", "oznaczenie" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "regulacja");
        }
    }
}
