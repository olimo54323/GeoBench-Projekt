using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeoBench.Core.Data.Migrations.SqlServer
{
    /// <inheritdoc />
    public partial class WynikiNakladania : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "budynki_dzialek_pow",
                columns: table => new
                {
                    oid = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    wariant = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    id_dzialki = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    id_budynku = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    pole_m2 = table.Column<double>(type: "float", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_budynki_dzialek_pow", x => x.oid);
                });

            migrationBuilder.CreateTable(
                name: "budynki_stref_pow",
                columns: table => new
                {
                    oid = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    wariant = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    id_budynku = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    oznaczenie = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    pole_m2 = table.Column<double>(type: "float", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_budynki_stref_pow", x => x.oid);
                });

            migrationBuilder.CreateTable(
                name: "dzialki_stref_pow",
                columns: table => new
                {
                    oid = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    wariant = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    id_dzialki = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    oznaczenie = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    pole_m2 = table.Column<double>(type: "float", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_dzialki_stref_pow", x => x.oid);
                });

            migrationBuilder.CreateIndex(
                name: "ix_budynki_dzialek_pow_wariant_id_dzialki",
                table: "budynki_dzialek_pow",
                columns: new[] { "wariant", "id_dzialki" });

            migrationBuilder.CreateIndex(
                name: "ix_budynki_stref_pow_wariant_id_budynku",
                table: "budynki_stref_pow",
                columns: new[] { "wariant", "id_budynku" });

            migrationBuilder.CreateIndex(
                name: "ix_dzialki_stref_pow_wariant_id_dzialki",
                table: "dzialki_stref_pow",
                columns: new[] { "wariant", "id_dzialki" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "budynki_dzialek_pow");

            migrationBuilder.DropTable(
                name: "budynki_stref_pow");

            migrationBuilder.DropTable(
                name: "dzialki_stref_pow");
        }
    }
}
