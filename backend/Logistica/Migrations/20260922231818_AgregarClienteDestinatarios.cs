using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Logistica.Migrations
{
    /// <inheritdoc />
    public partial class AgregarClienteDestinatarios : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "clientes_destinatarios",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    cliente_id = table.Column<int>(type: "integer", nullable: false),
                    nombre = table.Column<string>(type: "text", nullable: false),
                    telefono = table.Column<string>(type: "text", nullable: false),
                    destino_ubicacion_id = table.Column<long>(type: "bigint", nullable: false),
                    observaciones = table.Column<string>(type: "text", nullable: true),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_clientes_destinatarios", x => x.id);
                    table.ForeignKey(
                        name: "FK_clientes_destinatarios_clientes_cliente_id",
                        column: x => x.cliente_id,
                        principalTable: "clientes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_clientes_destinatarios_ubicaciones_destino_ubicacion_id",
                        column: x => x.destino_ubicacion_id,
                        principalTable: "ubicaciones",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_clientes_destinatarios_cliente_id",
                table: "clientes_destinatarios",
                column: "cliente_id");

            migrationBuilder.CreateIndex(
                name: "IX_clientes_destinatarios_destino_ubicacion_id",
                table: "clientes_destinatarios",
                column: "destino_ubicacion_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "clientes_destinatarios");
        }
    }
}
