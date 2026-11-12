using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace GeoBench.Core.Data.Migrations.Postgres
{
    /// <inheritdoc />
    public partial class WynikiScenariuszy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "budynki_stref",
                columns: table => new
                {
                    oid = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    wariant = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    id_budynku = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    oznaczenie = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_budynki_stref", x => x.oid);
                });

            migrationBuilder.CreateIndex(
                name: "ix_budynki_stref_wariant_id_budynku",
                table: "budynki_stref",
                columns: new[] { "wariant", "id_budynku" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "budynki_stref");
        }
    }
}
