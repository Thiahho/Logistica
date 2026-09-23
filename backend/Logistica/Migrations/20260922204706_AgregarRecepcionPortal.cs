using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Logistica.Migrations
{
    /// <inheritdoc />
    public partial class AgregarRecepcionPortal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_pedidos_usuarios_precio_manual_por",
                table: "pedidos");

            migrationBuilder.AddColumn<int>(
                name: "bultos_declarados_cliente",
                table: "pedidos",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "recepcion_confirmada_en",
                table: "pedidos",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "recepcion_confirmada_por",
                table: "pedidos",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_pedidos_recepcion_confirmada_por",
                table: "pedidos",
                column: "recepcion_confirmada_por");

            migrationBuilder.AddForeignKey(
                name: "FK_pedidos_usuarios_recepcion_confirmada_por",
                table: "pedidos",
                column: "recepcion_confirmada_por",
                principalTable: "usuarios",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_pedidos_usuarios_recepcion_confirmada_por",
                table: "pedidos");

            migrationBuilder.DropIndex(
                name: "IX_pedidos_recepcion_confirmada_por",
                table: "pedidos");

            migrationBuilder.DropColumn(
                name: "bultos_declarados_cliente",
                table: "pedidos");

            migrationBuilder.DropColumn(
                name: "recepcion_confirmada_en",
                table: "pedidos");

            migrationBuilder.DropColumn(
                name: "recepcion_confirmada_por",
                table: "pedidos");

            migrationBuilder.AddForeignKey(
                name: "FK_pedidos_usuarios_precio_manual_por",
                table: "pedidos",
                column: "precio_manual_por",
                principalTable: "usuarios",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
