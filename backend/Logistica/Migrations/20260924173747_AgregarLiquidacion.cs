using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Logistica.Migrations
{
    /// <inheritdoc />
    public partial class AgregarLiquidacion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "liq_bono",
                table: "rutas",
                type: "numeric(12,2)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "liq_entregas",
                table: "rutas",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "liq_fallidas_imputables",
                table: "rutas",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "liq_pago_entregas",
                table: "rutas",
                type: "numeric(12,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "liq_pct_exito",
                table: "rutas",
                type: "numeric(5,2)",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "liquidacion_id",
                table: "rutas",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "pago_ajuste_motivo",
                table: "rutas",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "liquidaciones",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    repartidor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    desde = table.Column<DateOnly>(type: "date", nullable: false),
                    hasta = table.Column<DateOnly>(type: "date", nullable: false),
                    cantidad_rutas = table.Column<int>(type: "integer", nullable: false),
                    total = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    nota = table.Column<string>(type: "text", nullable: true),
                    emitida_por = table.Column<Guid>(type: "uuid", nullable: false),
                    emitida_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_liquidaciones", x => x.id);
                    table.CheckConstraint("ck_liquidaciones_periodo", "hasta >= desde");
                    table.CheckConstraint("ck_liquidaciones_rutas", "cantidad_rutas > 0");
                    table.ForeignKey(
                        name: "FK_liquidaciones_usuarios_emitida_por",
                        column: x => x.emitida_por,
                        principalTable: "usuarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_liquidaciones_usuarios_repartidor_id",
                        column: x => x.repartidor_id,
                        principalTable: "usuarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "parametros_liquidacion",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    tipo_vehiculo = table.Column<string>(type: "text", nullable: false),
                    pago_por_entrega = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    bono_ruta = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    pct_minimo_exitosas = table.Column<decimal>(type: "numeric(5,2)", nullable: false),
                    motivos_imputables = table.Column<string[]>(type: "text[]", nullable: false, defaultValueSql: "'{}'::text[]"),
                    vigente_desde = table.Column<DateOnly>(type: "date", nullable: false),
                    vigente_hasta = table.Column<DateOnly>(type: "date", nullable: true),
                    creado_por = table.Column<Guid>(type: "uuid", nullable: true),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_parametros_liquidacion", x => x.id);
                    table.CheckConstraint("ck_parametros_liquidacion_montos", "pago_por_entrega >= 0 and bono_ruta >= 0");
                    table.CheckConstraint("ck_parametros_liquidacion_pct", "pct_minimo_exitosas between 0 and 100");
                    table.CheckConstraint("ck_parametros_liquidacion_tipo_vehiculo", "tipo_vehiculo in ('camioneta','moto')");
                    table.CheckConstraint("ck_parametros_liquidacion_vigencia", "vigente_hasta is null or vigente_hasta >= vigente_desde");
                    table.ForeignKey(
                        name: "FK_parametros_liquidacion_usuarios_creado_por",
                        column: x => x.creado_por,
                        principalTable: "usuarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_rutas_liquidacion_id",
                table: "rutas",
                column: "liquidacion_id");

            migrationBuilder.CreateIndex(
                name: "IX_liquidaciones_emitida_por",
                table: "liquidaciones",
                column: "emitida_por");

            migrationBuilder.CreateIndex(
                name: "IX_liquidaciones_repartidor_id_desde",
                table: "liquidaciones",
                columns: new[] { "repartidor_id", "desde" });

            migrationBuilder.CreateIndex(
                name: "IX_parametros_liquidacion_creado_por",
                table: "parametros_liquidacion",
                column: "creado_por");

            migrationBuilder.CreateIndex(
                name: "IX_parametros_liquidacion_tipo_vehiculo_vigente_desde",
                table: "parametros_liquidacion",
                columns: new[] { "tipo_vehiculo", "vigente_desde" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_parametros_liquidacion_vigente",
                table: "parametros_liquidacion",
                column: "tipo_vehiculo",
                unique: true,
                filter: "vigente_hasta is null");

            migrationBuilder.AddForeignKey(
                name: "FK_rutas_liquidaciones_liquidacion_id",
                table: "rutas",
                column: "liquidacion_id",
                principalTable: "liquidaciones",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            // B4 (acta RF-41, changelog 4.21): una liquidación emitida es un comprobante — no se edita ni se
            // borra, mismo criterio que trg_facturas_inmutable. Funciones propias: el RAISE nombra la tabla.
            migrationBuilder.Sql("""
                create or replace function fn_liquidaciones_inmutable()
                returns trigger language plpgsql as $$
                begin
                  raise exception 'Liquidación %: una liquidación emitida no se edita ni se borra. Las correcciones van en la liquidación siguiente.',
                    coalesce(old.id, new.id);
                end $$;

                create trigger trg_liquidaciones_inmutable
                  before update or delete on liquidaciones
                  for each row execute function fn_liquidaciones_inmutable();
                """);

            // Una ruta ya liquidada no cambia de liquidación, de pago ni de desglose: si cambiara, el
            // comprobante dejaría de coincidir con lo que dice la ruta. Vincularla (null -> id) sí se puede.
            migrationBuilder.Sql("""
                create or replace function fn_rutas_liquidada()
                returns trigger language plpgsql as $$
                begin
                  if tg_op = 'DELETE' then
                    raise exception 'Ruta %: ya está en la liquidación % y no se puede borrar.', old.id, old.liquidacion_id;
                  end if;
                  if new.liquidacion_id is distinct from old.liquidacion_id
                     or new.pago_repartidor is distinct from old.pago_repartidor
                     or new.liq_entregas is distinct from old.liq_entregas
                     or new.liq_fallidas_imputables is distinct from old.liq_fallidas_imputables
                     or new.liq_pct_exito is distinct from old.liq_pct_exito
                     or new.liq_pago_entregas is distinct from old.liq_pago_entregas
                     or new.liq_bono is distinct from old.liq_bono
                     or new.pago_ajuste_motivo is distinct from old.pago_ajuste_motivo then
                    raise exception 'Ruta %: ya está en la liquidación %; su pago no se modifica. Las correcciones van en la liquidación siguiente.',
                      old.id, old.liquidacion_id;
                  end if;
                  return new;
                end $$;

                create trigger trg_rutas_liquidada
                  before update or delete on rutas
                  for each row when (old.liquidacion_id is not null)
                  execute function fn_rutas_liquidada();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                drop trigger if exists trg_rutas_liquidada on rutas;
                drop function if exists fn_rutas_liquidada();
                drop trigger if exists trg_liquidaciones_inmutable on liquidaciones;
                drop function if exists fn_liquidaciones_inmutable();
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_rutas_liquidaciones_liquidacion_id",
                table: "rutas");

            migrationBuilder.DropTable(
                name: "liquidaciones");

            migrationBuilder.DropTable(
                name: "parametros_liquidacion");

            migrationBuilder.DropIndex(
                name: "IX_rutas_liquidacion_id",
                table: "rutas");

            migrationBuilder.DropColumn(
                name: "liq_bono",
                table: "rutas");

            migrationBuilder.DropColumn(
                name: "liq_entregas",
                table: "rutas");

            migrationBuilder.DropColumn(
                name: "liq_fallidas_imputables",
                table: "rutas");

            migrationBuilder.DropColumn(
                name: "liq_pago_entregas",
                table: "rutas");

            migrationBuilder.DropColumn(
                name: "liq_pct_exito",
                table: "rutas");

            migrationBuilder.DropColumn(
                name: "liquidacion_id",
                table: "rutas");

            migrationBuilder.DropColumn(
                name: "pago_ajuste_motivo",
                table: "rutas");
        }
    }
}
