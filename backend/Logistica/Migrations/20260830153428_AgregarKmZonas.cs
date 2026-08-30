using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Logistica.Migrations
{
    /// <inheritdoc />
    public partial class AgregarKmZonas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "km_desde",
                table: "zonas",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "km_hasta",
                table: "zonas",
                type: "integer",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_zonas_km_desde",
                table: "zonas",
                sql: "km_desde is null or km_desde >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_zonas_km_rango",
                table: "zonas",
                sql: "km_hasta is null or km_desde is null or km_hasta > km_desde");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_zonas_km_desde",
                table: "zonas");

            migrationBuilder.DropCheckConstraint(
                name: "ck_zonas_km_rango",
                table: "zonas");

            migrationBuilder.DropColumn(
                name: "km_desde",
                table: "zonas");

            migrationBuilder.DropColumn(
                name: "km_hasta",
                table: "zonas");
        }
    }
}
