using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Logistica.Migrations
{
    /// <inheritdoc />
    public partial class AgregarRetiroDeRuta : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "cerrada_por",
                table: "rutas",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "cierre_repartidor_combustible",
                table: "rutas",
                type: "numeric(12,2)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "cierre_repartidor_device_uuid",
                table: "rutas",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "cierre_repartidor_en",
                table: "rutas",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "cierre_repartidor_km_final",
                table: "rutas",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "cierre_repartidor_notas",
                table: "rutas",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "cierre_repartidor_peajes",
                table: "rutas",
                type: "numeric(12,2)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "retiro_bultos_contados",
                table: "rutas",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "retiro_bultos_esperados",
                table: "rutas",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "retiro_confirmado_en",
                table: "rutas",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "retiro_device_uuid",
                table: "rutas",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "retiro_firma_path",
                table: "rutas",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "retiro_km_inicial",
                table: "rutas",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "retiro_observaciones",
                table: "rutas",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_rutas_cerrada_por",
                table: "rutas",
                column: "cerrada_por");

            migrationBuilder.AddCheckConstraint(
                name: "ck_rutas_cierre_repartidor_montos",
                table: "rutas",
                sql: "(cierre_repartidor_km_final is null or cierre_repartidor_km_final >= 0) and (cierre_repartidor_combustible is null or cierre_repartidor_combustible >= 0) and (cierre_repartidor_peajes is null or cierre_repartidor_peajes >= 0)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_rutas_retiro_bultos",
                table: "rutas",
                sql: "(retiro_bultos_esperados is null or retiro_bultos_esperados >= 0) and (retiro_bultos_contados is null or retiro_bultos_contados >= 0)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_rutas_retiro_discrepancia_observada",
                table: "rutas",
                sql: "retiro_confirmado_en is null or retiro_bultos_contados = retiro_bultos_esperados or (retiro_observaciones is not null and btrim(retiro_observaciones) <> '')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_rutas_retiro_km_inicial",
                table: "rutas",
                sql: "retiro_km_inicial is null or retiro_km_inicial >= 0");

            migrationBuilder.AddForeignKey(
                name: "FK_rutas_usuarios_cerrada_por",
                table: "rutas",
                column: "cerrada_por",
                principalTable: "usuarios",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            // Acta changelog 4.7, §7 ("el número de la calle no se reescribe") y construccion_v1.md
            // §3 regla 3: lo que el repartidor firmó al retirar y declaró al cerrar su jornada no
            // se edita, ni desde el back-office ni desde la PWA. Va como trigger y no como
            // validación en C# por la misma razón que fn_congelar_pedido con el precio: un dato
            // que quien lo recibe puede reescribir no es evidencia de nada, y el chequeo tiene que
            // valer también para un UPDATE a mano en la base.
            //
            // Función propia y no fn_log_inmutable: esa tiene "pedido_eventos" hardcodeado en el
            // RAISE y reusarla daría un mensaje engañoso (mismo criterio ya aplicado en
            // AgregarCuentaCorriente con fn_facturas_inmutable).
            //
            // `is distinct from` y no `<>`: con nulls de por medio `<>` devuelve null, la condición
            // nunca da true y el trigger no protegería nada. Mismo operador que fn_congelar_pedido.
            migrationBuilder.Sql("""
                create or replace function fn_congelar_declaracion_repartidor()
                returns trigger language plpgsql as $$
                begin
                  if old.retiro_confirmado_en is not null and (
                       new.retiro_confirmado_en    is distinct from old.retiro_confirmado_en
                    or new.retiro_bultos_esperados is distinct from old.retiro_bultos_esperados
                    or new.retiro_bultos_contados  is distinct from old.retiro_bultos_contados
                    or new.retiro_observaciones    is distinct from old.retiro_observaciones
                    or new.retiro_firma_path       is distinct from old.retiro_firma_path
                    or new.retiro_km_inicial       is distinct from old.retiro_km_inicial
                    or new.retiro_device_uuid      is distinct from old.retiro_device_uuid
                  ) then
                    raise exception
                      'Ruta %: el retiro firmado por el repartidor no se edita (acta §7). El cierre de administración corrige sus propias columnas, no la declaración de la calle.', old.id;
                  end if;

                  if old.cierre_repartidor_en is not null and (
                       new.cierre_repartidor_en          is distinct from old.cierre_repartidor_en
                    or new.cierre_repartidor_km_final    is distinct from old.cierre_repartidor_km_final
                    or new.cierre_repartidor_combustible is distinct from old.cierre_repartidor_combustible
                    or new.cierre_repartidor_peajes      is distinct from old.cierre_repartidor_peajes
                    or new.cierre_repartidor_notas       is distinct from old.cierre_repartidor_notas
                    or new.cierre_repartidor_device_uuid is distinct from old.cierre_repartidor_device_uuid
                  ) then
                    raise exception
                      'Ruta %: el cierre de jornada del repartidor no se edita (acta §7). Para cerrar con otro número, administración lo hace en sus propias columnas con notas_cierre obligatorio.', old.id;
                  end if;

                  return new;
                end $$;
                """);
            migrationBuilder.Sql("""
                create trigger trg_congelar_declaracion_repartidor
                  before update on rutas
                  for each row execute function fn_congelar_declaracion_repartidor();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("drop trigger if exists trg_congelar_declaracion_repartidor on rutas;");
            migrationBuilder.Sql("drop function if exists fn_congelar_declaracion_repartidor();");

            migrationBuilder.DropForeignKey(
                name: "FK_rutas_usuarios_cerrada_por",
                table: "rutas");

            migrationBuilder.DropIndex(
                name: "IX_rutas_cerrada_por",
                table: "rutas");

            migrationBuilder.DropCheckConstraint(
                name: "ck_rutas_cierre_repartidor_montos",
                table: "rutas");

            migrationBuilder.DropCheckConstraint(
                name: "ck_rutas_retiro_bultos",
                table: "rutas");

            migrationBuilder.DropCheckConstraint(
                name: "ck_rutas_retiro_discrepancia_observada",
                table: "rutas");

            migrationBuilder.DropCheckConstraint(
                name: "ck_rutas_retiro_km_inicial",
                table: "rutas");

            migrationBuilder.DropColumn(
                name: "cerrada_por",
                table: "rutas");

            migrationBuilder.DropColumn(
                name: "cierre_repartidor_combustible",
                table: "rutas");

            migrationBuilder.DropColumn(
                name: "cierre_repartidor_device_uuid",
                table: "rutas");

            migrationBuilder.DropColumn(
                name: "cierre_repartidor_en",
                table: "rutas");

            migrationBuilder.DropColumn(
                name: "cierre_repartidor_km_final",
                table: "rutas");

            migrationBuilder.DropColumn(
                name: "cierre_repartidor_notas",
                table: "rutas");

            migrationBuilder.DropColumn(
                name: "cierre_repartidor_peajes",
                table: "rutas");

            migrationBuilder.DropColumn(
                name: "retiro_bultos_contados",
                table: "rutas");

            migrationBuilder.DropColumn(
                name: "retiro_bultos_esperados",
                table: "rutas");

            migrationBuilder.DropColumn(
                name: "retiro_confirmado_en",
                table: "rutas");

            migrationBuilder.DropColumn(
                name: "retiro_device_uuid",
                table: "rutas");

            migrationBuilder.DropColumn(
                name: "retiro_firma_path",
                table: "rutas");

            migrationBuilder.DropColumn(
                name: "retiro_km_inicial",
                table: "rutas");

            migrationBuilder.DropColumn(
                name: "retiro_observaciones",
                table: "rutas");
        }
    }
}
