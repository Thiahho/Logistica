using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Logistica.Migrations
{
    /// <inheritdoc />
    public partial class AgregarDocumentoAPruebaEntrega : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "documento_numero",
                table: "pruebas_entrega",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "sin_documento_motivo",
                table: "pruebas_entrega",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "documento_numero",
                table: "pruebas_entrega");

            migrationBuilder.DropColumn(
                name: "sin_documento_motivo",
                table: "pruebas_entrega");
        }
    }
}
