using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Logistica.Migrations
{
    /// <inheritdoc />
    public partial class PortalDuenoYEmpleados : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "creado_por_cliente_usuario_id",
                table: "pedidos",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "rol",
                table: "clientes_usuarios",
                type: "text",
                nullable: false,
                defaultValue: "dueno");

            migrationBuilder.CreateTable(
                name: "clientes_usuarios_actividad",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    cliente_id = table.Column<int>(type: "integer", nullable: false),
                    cliente_usuario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    accion = table.Column<string>(type: "text", nullable: false),
                    entidad_tipo = table.Column<string>(type: "text", nullable: true),
                    entidad_id = table.Column<string>(type: "text", nullable: true),
                    detalle = table.Column<string>(type: "text", nullable: true),
                    ocurrido_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_clientes_usuarios_actividad", x => x.id);
                    table.ForeignKey(
                        name: "FK_clientes_usuarios_actividad_clientes_cliente_id",
                        column: x => x.cliente_id,
                        principalTable: "clientes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_clientes_usuarios_actividad_clientes_usuarios_cliente_usuar~",
                        column: x => x.cliente_usuario_id,
                        principalTable: "clientes_usuarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_pedidos_creado_por_cliente_usuario_id",
                table: "pedidos",
                column: "creado_por_cliente_usuario_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_clientes_usuarios_rol",
                table: "clientes_usuarios",
                sql: "rol in ('dueno','usuario')");

            migrationBuilder.CreateIndex(
                name: "IX_clientes_usuarios_actividad_cliente_id_ocurrido_en",
                table: "clientes_usuarios_actividad",
                columns: new[] { "cliente_id", "ocurrido_en" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_clientes_usuarios_actividad_cliente_usuario_id",
                table: "clientes_usuarios_actividad",
                column: "cliente_usuario_id");

            migrationBuilder.AddForeignKey(
                name: "FK_pedidos_clientes_usuarios_creado_por_cliente_usuario_id",
                table: "pedidos",
                column: "creado_por_cliente_usuario_id",
                principalTable: "clientes_usuarios",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            // Pedidos de portal ya cargados: el autor solo quedó en precio_manual_por cuando hubo
            // cotización (B5). Se recupera ese caso; los B9 (sin tarifa) quedan sin autor. Toca solo la
            // columna nueva: ni fn_congelar_pedido ni fn_log_estado_pedido reaccionan a ella.
            migrationBuilder.Sql(@"
update pedidos p
   set creado_por_cliente_usuario_id = p.precio_manual_por
 where p.origen_carga = 'portal'
   and p.creado_por_cliente_usuario_id is null
   and exists (select 1 from clientes_usuarios cu
                where cu.id = p.precio_manual_por and cu.cliente_id = p.cliente_id);");

            // El historial de actividad es de solo inserción, igual que pedido_eventos (RF-28).
            migrationBuilder.Sql(@"
create or replace function fn_actividad_portal_inmutable()
returns trigger language plpgsql as $$
begin
  raise exception 'clientes_usuarios_actividad es de solo inserción';
end $$;");
            migrationBuilder.Sql(
                "create trigger trg_actividad_portal_inmutable " +
                "before update or delete on clientes_usuarios_actividad " +
                "for each row execute function fn_actividad_portal_inmutable();");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("drop trigger if exists trg_actividad_portal_inmutable on clientes_usuarios_actividad;");
            migrationBuilder.Sql("drop function if exists fn_actividad_portal_inmutable();");

            migrationBuilder.DropForeignKey(
                name: "FK_pedidos_clientes_usuarios_creado_por_cliente_usuario_id",
                table: "pedidos");

            migrationBuilder.DropTable(
                name: "clientes_usuarios_actividad");

            migrationBuilder.DropIndex(
                name: "IX_pedidos_creado_por_cliente_usuario_id",
                table: "pedidos");

            migrationBuilder.DropCheckConstraint(
                name: "ck_clientes_usuarios_rol",
                table: "clientes_usuarios");

            migrationBuilder.DropColumn(
                name: "creado_por_cliente_usuario_id",
                table: "pedidos");

            migrationBuilder.DropColumn(
                name: "rol",
                table: "clientes_usuarios");
        }
    }
}
