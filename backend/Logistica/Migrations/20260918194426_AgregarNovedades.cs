using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Logistica.Migrations
{
    /// <inheritdoc />
    public partial class AgregarNovedades : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_ruta_paradas_estado",
                table: "ruta_paradas");

            migrationBuilder.CreateTable(
                name: "novedades",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ruta_id = table.Column<long>(type: "bigint", nullable: false),
                    parada_id = table.Column<long>(type: "bigint", nullable: true),
                    pedido_id = table.Column<long>(type: "bigint", nullable: true),
                    tipo = table.Column<string>(type: "text", nullable: false),
                    origen = table.Column<string>(type: "text", nullable: false),
                    categoria = table.Column<string>(type: "text", nullable: true),
                    descripcion = table.Column<string>(type: "text", nullable: false),
                    propuesta_campo = table.Column<string>(type: "text", nullable: true),
                    propuesta_valor_anterior = table.Column<string>(type: "text", nullable: true),
                    propuesta_valor_nuevo = table.Column<string>(type: "text", nullable: true),
                    foto_path = table.Column<string>(type: "text", nullable: true),
                    device_uuid = table.Column<string>(type: "text", nullable: true),
                    estado = table.Column<string>(type: "text", nullable: false, defaultValue: "abierta"),
                    creada_por = table.Column<Guid>(type: "uuid", nullable: false),
                    creada_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    resuelta_por = table.Column<Guid>(type: "uuid", nullable: true),
                    resuelta_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    resolucion = table.Column<string>(type: "text", nullable: true),
                    visto_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_novedades", x => x.id);
                    table.CheckConstraint("ck_novedades_estado", "estado in ('abierta','resuelta','rechazada')");
                    table.CheckConstraint("ck_novedades_origen", "origen in ('repartidor','operacion')");
                    table.CheckConstraint("ck_novedades_origen_coherente", "(origen = 'repartidor' and tipo in ('incidencia_ruta','problema_carga','cambio_propuesto')) or (origen = 'operacion' and tipo in ('cambio_operacion','cancelacion'))");
                    table.CheckConstraint("ck_novedades_propuesta", "(tipo = 'cambio_propuesto') = (propuesta_campo is not null and propuesta_valor_nuevo is not null)");
                    table.CheckConstraint("ck_novedades_tipo", "tipo in ('incidencia_ruta','problema_carga','cambio_propuesto','cambio_operacion','cancelacion')");
                    table.ForeignKey(
                        name: "FK_novedades_pedidos_pedido_id",
                        column: x => x.pedido_id,
                        principalTable: "pedidos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_novedades_ruta_paradas_parada_id",
                        column: x => x.parada_id,
                        principalTable: "ruta_paradas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_novedades_rutas_ruta_id",
                        column: x => x.ruta_id,
                        principalTable: "rutas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_novedades_usuarios_creada_por",
                        column: x => x.creada_por,
                        principalTable: "usuarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_novedades_usuarios_resuelta_por",
                        column: x => x.resuelta_por,
                        principalTable: "usuarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_ruta_paradas_estado",
                table: "ruta_paradas",
                sql: "estado in ('pendiente','completada','fallida','cancelada')");

            migrationBuilder.CreateIndex(
                name: "ix_novedades_abiertas",
                table: "novedades",
                column: "estado",
                filter: "estado = 'abierta'");

            migrationBuilder.CreateIndex(
                name: "IX_novedades_creada_por",
                table: "novedades",
                column: "creada_por");

            migrationBuilder.CreateIndex(
                name: "IX_novedades_parada_id",
                table: "novedades",
                column: "parada_id");

            migrationBuilder.CreateIndex(
                name: "IX_novedades_pedido_id",
                table: "novedades",
                column: "pedido_id");

            migrationBuilder.CreateIndex(
                name: "IX_novedades_resuelta_por",
                table: "novedades",
                column: "resuelta_por");

            migrationBuilder.CreateIndex(
                name: "IX_novedades_ruta_id_device_uuid",
                table: "novedades",
                columns: new[] { "ruta_id", "device_uuid" },
                unique: true,
                filter: "device_uuid is not null");

            migrationBuilder.CreateIndex(
                name: "IX_novedades_ruta_id_estado",
                table: "novedades",
                columns: new[] { "ruta_id", "estado" });

            // ============ VISTA PARA LA PWA: pedido_estado ============
            // La PWA necesita saber si operación canceló un pedido con la ruta en curso. Es un
            // estado, no un importe: la regla 3.4 (v_paradas_repartidor no tiene precios) sigue
            // intacta. CREATE OR REPLACE VIEW solo admite columnas nuevas AL FINAL, por eso va última.
            migrationBuilder.Sql(@"
create or replace view v_paradas_repartidor as
select rp.id as parada_id, rp.ruta_id, rp.orden, rp.tipo, rp.estado,
       rp.llegada_en, rp.salida_en,
       p.id as pedido_id, p.destinatario_nombre, p.destinatario_telefono,
       p.bultos, p.observaciones,
       u.calle_numero, u.referencia, u.lat, u.lng, l.nombre as localidad,
       p.estado as pedido_estado
from ruta_paradas rp
join parada_pedidos pp on pp.parada_id = rp.id
join pedidos p         on p.id = pp.pedido_id
join ubicaciones u     on u.id = rp.ubicacion_id
left join localidades l on l.id = u.localidad_id;");

            // ============ INMUTABILIDAD DEL REPORTE (regla 3.3) ============
            // Mismo principio que trg_congelar_declaracion_repartidor (acta §7, "el número de la calle
            // no se reescribe"): lo que informó quien creó la novedad no se edita después. Solo se
            // completan estado, resolución y visto_en. `is distinct from` y no `<>`: con nulls de por
            // medio `<>` devuelve null y la condición nunca dispara. Mensaje propio, no fn_log_inmutable.
            migrationBuilder.Sql(@"
create or replace function fn_novedades_inmutable()
returns trigger language plpgsql as $$
begin
  if new.ruta_id                  is distinct from old.ruta_id
  or new.parada_id                is distinct from old.parada_id
  or new.pedido_id                is distinct from old.pedido_id
  or new.tipo                     is distinct from old.tipo
  or new.origen                   is distinct from old.origen
  or new.categoria                is distinct from old.categoria
  or new.descripcion              is distinct from old.descripcion
  or new.propuesta_campo          is distinct from old.propuesta_campo
  or new.propuesta_valor_anterior is distinct from old.propuesta_valor_anterior
  or new.propuesta_valor_nuevo    is distinct from old.propuesta_valor_nuevo
  or new.foto_path                is distinct from old.foto_path
  or new.device_uuid              is distinct from old.device_uuid
  or new.creada_por               is distinct from old.creada_por
  or new.creada_en                is distinct from old.creada_en then
    raise exception 'Novedad %: lo que informó quien la creó no se edita. Solo se resuelve o se marca como vista.', old.id;
  end if;
  if old.estado <> 'abierta' and new.estado is distinct from old.estado then
    raise exception 'Novedad %: ya está % y no vuelve a abrirse.', old.id, old.estado;
  end if;
  return new;
end $$;");

            migrationBuilder.Sql(@"
create trigger trg_novedades_inmutable
  before update on novedades
  for each row execute function fn_novedades_inmutable();");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("drop trigger if exists trg_novedades_inmutable on novedades;");
            migrationBuilder.Sql("drop function if exists fn_novedades_inmutable();");

            // Postgres no permite sacar una columna con CREATE OR REPLACE VIEW: se recrea.
            migrationBuilder.Sql("drop view if exists v_paradas_repartidor;");
            migrationBuilder.Sql(@"
create view v_paradas_repartidor as
select rp.id as parada_id, rp.ruta_id, rp.orden, rp.tipo, rp.estado,
       rp.llegada_en, rp.salida_en,
       p.id as pedido_id, p.destinatario_nombre, p.destinatario_telefono,
       p.bultos, p.observaciones,
       u.calle_numero, u.referencia, u.lat, u.lng, l.nombre as localidad
from ruta_paradas rp
join parada_pedidos pp on pp.parada_id = rp.id
join pedidos p         on p.id = pp.pedido_id
join ubicaciones u     on u.id = rp.ubicacion_id
left join localidades l on l.id = u.localidad_id;");

            migrationBuilder.DropTable(
                name: "novedades");

            migrationBuilder.DropCheckConstraint(
                name: "ck_ruta_paradas_estado",
                table: "ruta_paradas");

            migrationBuilder.AddCheckConstraint(
                name: "ck_ruta_paradas_estado",
                table: "ruta_paradas",
                sql: "estado in ('pendiente','completada','fallida')");
        }
    }
}
