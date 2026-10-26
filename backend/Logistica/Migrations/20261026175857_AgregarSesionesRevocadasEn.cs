using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Logistica.Migrations
{
    /// <inheritdoc />
    public partial class AgregarSesionesRevocadasEn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "sesiones_revocadas_en",
                table: "usuarios",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "sesiones_revocadas_en",
                table: "clientes_usuarios",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "sesiones_revocadas_en",
                table: "usuarios");

            migrationBuilder.DropColumn(
                name: "sesiones_revocadas_en",
                table: "clientes_usuarios");
        }
    }
}
