using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Logistica.Migrations
{
    /// <inheritdoc />
    public partial class AgregarViajes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "orden_en_viaje",
                table: "pedidos",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "viaje_id",
                table: "pedidos",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "viajes",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    cliente_id = table.Column<int>(type: "integer", nullable: false),
                    fecha_entrega = table.Column<DateOnly>(type: "date", nullable: false),
                    origen_ubicacion_id = table.Column<long>(type: "bigint", nullable: false),
                    tipo_vehiculo = table.Column<string>(type: "text", nullable: true),
                    estado = table.Column<string>(type: "text", nullable: false, defaultValue: "activo"),
                    km_estimados = table.Column<decimal>(type: "numeric(8,2)", nullable: true),
                    ruta_id = table.Column<long>(type: "bigint", nullable: true),
                    creado_por_cliente_usuario_id = table.Column<Guid>(type: "uuid", nullable: true),
                    creado_por_usuario_id = table.Column<Guid>(type: "uuid", nullable: true),
                    observaciones = table.Column<string>(type: "text", nullable: true),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_viajes", x => x.id);
                    table.CheckConstraint("ck_viajes_creador", "(creado_por_cliente_usuario_id is null) <> (creado_por_usuario_id is null)");
                    table.CheckConstraint("ck_viajes_estado", "estado in ('activo','cancelado')");
                    table.CheckConstraint("ck_viajes_tipo_vehiculo", "tipo_vehiculo is null or tipo_vehiculo in ('camioneta','moto')");
                    table.ForeignKey(
                        name: "FK_viajes_clientes_cliente_id",
                        column: x => x.cliente_id,
                        principalTable: "clientes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_viajes_clientes_usuarios_creado_por_cliente_usuario_id",
                        column: x => x.creado_por_cliente_usuario_id,
                        principalTable: "clientes_usuarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_viajes_rutas_ruta_id",
                        column: x => x.ruta_id,
                        principalTable: "rutas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_viajes_ubicaciones_origen_ubicacion_id",
                        column: x => x.origen_ubicacion_id,
                        principalTable: "ubicaciones",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_viajes_usuarios_creado_por_usuario_id",
                        column: x => x.creado_por_usuario_id,
                        principalTable: "usuarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_pedidos_viaje_id",
                table: "pedidos",
                column: "viaje_id");

            migrationBuilder.CreateIndex(
                name: "IX_viajes_cliente_id_fecha_entrega",
                table: "viajes",
                columns: new[] { "cliente_id", "fecha_entrega" });

            migrationBuilder.CreateIndex(
                name: "IX_viajes_creado_por_cliente_usuario_id",
                table: "viajes",
                column: "creado_por_cliente_usuario_id");

            migrationBuilder.CreateIndex(
                name: "IX_viajes_creado_por_usuario_id",
                table: "viajes",
                column: "creado_por_usuario_id");

            migrationBuilder.CreateIndex(
                name: "IX_viajes_origen_ubicacion_id",
                table: "viajes",
                column: "origen_ubicacion_id");

            migrationBuilder.CreateIndex(
                name: "IX_viajes_ruta_id",
                table: "viajes",
                column: "ruta_id");

            migrationBuilder.AddForeignKey(
                name: "FK_pedidos_viajes_viaje_id",
                table: "pedidos",
                column: "viaje_id",
                principalTable: "viajes",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_pedidos_viajes_viaje_id",
                table: "pedidos");

            migrationBuilder.DropTable(
                name: "viajes");

            migrationBuilder.DropIndex(
                name: "IX_pedidos_viaje_id",
                table: "pedidos");

            migrationBuilder.DropColumn(
                name: "orden_en_viaje",
                table: "pedidos");

            migrationBuilder.DropColumn(
                name: "viaje_id",
                table: "pedidos");
        }
    }
}
