using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Logistica.Migrations
{
    /// <inheritdoc />
    public partial class AgregarOrigenRuta : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "origen_ubicacion_id",
                table: "rutas",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_rutas_origen_ubicacion_id",
                table: "rutas",
                column: "origen_ubicacion_id");

            migrationBuilder.AddForeignKey(
                name: "FK_rutas_ubicaciones_origen_ubicacion_id",
                table: "rutas",
                column: "origen_ubicacion_id",
                principalTable: "ubicaciones",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_rutas_ubicaciones_origen_ubicacion_id",
                table: "rutas");

            migrationBuilder.DropIndex(
                name: "IX_rutas_origen_ubicacion_id",
                table: "rutas");

            migrationBuilder.DropColumn(
                name: "origen_ubicacion_id",
                table: "rutas");
        }
    }
}
