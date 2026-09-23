using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Logistica.Migrations
{
    /// <inheritdoc />
    public partial class ZonasAutomaticas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "distancia_fuente",
                table: "localidades",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "distancia_km_deposito",
                table: "localidades",
                type: "numeric(7,1)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "lat",
                table: "localidades",
                type: "numeric(10,7)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "lng",
                table: "localidades",
                type: "numeric(10,7)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "zona_manual",
                table: "localidades",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Las zonas que ya estaban cargadas se fijaron a mano: quedan como manuales para que
            // ningún recálculo las cambie hasta que administración pida "volver a automática".
            migrationBuilder.Sql("update localidades set zona_manual = true where zona_id is not null;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "distancia_fuente",
                table: "localidades");

            migrationBuilder.DropColumn(
                name: "distancia_km_deposito",
                table: "localidades");

            migrationBuilder.DropColumn(
                name: "lat",
                table: "localidades");

            migrationBuilder.DropColumn(
                name: "lng",
                table: "localidades");

            migrationBuilder.DropColumn(
                name: "zona_manual",
                table: "localidades");
        }
    }
}
