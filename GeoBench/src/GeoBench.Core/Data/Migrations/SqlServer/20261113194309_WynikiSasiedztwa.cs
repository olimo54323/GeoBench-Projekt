using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeoBench.Core.Data.Migrations.SqlServer
{
    /// <inheritdoc />
    public partial class WynikiSasiedztwa : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "budynki_najblizsze",
                columns: table => new
                {
                    oid = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    wariant = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    id_budynku = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    id_sasiada = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    odleglosc_m = table.Column<double>(type: "float", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_budynki_najblizsze", x => x.oid);
                });

            migrationBuilder.CreateTable(
                name: "dzialki_budynki_50m",
                columns: table => new
                {
                    oid = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    wariant = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    id_dzialki = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    liczba_budynkow = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_dzialki_budynki_50m", x => x.oid);
                });

            migrationBuilder.CreateTable(
                name: "geometrie_naprawione",
                columns: table => new
                {
                    oid = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    wariant = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    warstwa = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    id_obiektu = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    pole_m2 = table.Column<double>(type: "float", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_geometrie_naprawione", x => x.oid);
                });

            migrationBuilder.CreateIndex(
                name: "ix_budynki_najblizsze_wariant_id_budynku",
                table: "budynki_najblizsze",
                columns: new[] { "wariant", "id_budynku" });

            migrationBuilder.CreateIndex(
                name: "ix_dzialki_budynki_50m_wariant_id_dzialki",
                table: "dzialki_budynki_50m",
                columns: new[] { "wariant", "id_dzialki" });

            migrationBuilder.CreateIndex(
                name: "ix_geometrie_naprawione_wariant_warstwa_id_obiektu",
                table: "geometrie_naprawione",
                columns: new[] { "wariant", "warstwa", "id_obiektu" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "budynki_najblizsze");

            migrationBuilder.DropTable(
                name: "dzialki_budynki_50m");

            migrationBuilder.DropTable(
                name: "geometrie_naprawione");
        }
    }
}
