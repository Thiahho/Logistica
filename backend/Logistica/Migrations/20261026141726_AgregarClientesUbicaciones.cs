using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Logistica.Migrations
{
    /// <inheritdoc />
    public partial class AgregarClientesUbicaciones : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "clientes_ubicaciones",
                columns: table => new
                {
                    cliente_id = table.Column<int>(type: "integer", nullable: false),
                    ubicacion_id = table.Column<long>(type: "bigint", nullable: false),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_clientes_ubicaciones", x => new { x.cliente_id, x.ubicacion_id });
                    table.ForeignKey(
                        name: "FK_clientes_ubicaciones_clientes_cliente_id",
                        column: x => x.cliente_id,
                        principalTable: "clientes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_clientes_ubicaciones_ubicaciones_ubicacion_id",
                        column: x => x.ubicacion_id,
                        principalTable: "ubicaciones",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_clientes_ubicaciones_ubicacion_id",
                table: "clientes_ubicaciones",
                column: "ubicacion_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "clientes_ubicaciones");
        }
    }
}
