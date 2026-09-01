using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Logistica.Migrations
{
    /// <inheritdoc />
    public partial class AgregarTipoVehiculo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_tarifas_cliente_id_zona_id",
                table: "tarifas");

            migrationBuilder.AddColumn<string>(
                name: "tipo",
                table: "vehiculos",
                type: "text",
                nullable: false,
                defaultValue: "camioneta");

            migrationBuilder.AddColumn<string>(
                name: "tipo_vehiculo",
                table: "tarifas",
                type: "text",
                nullable: false,
                defaultValue: "camioneta");

            migrationBuilder.AlterColumn<decimal>(
                name: "total",
                table: "pedidos",
                type: "numeric(12,2)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(12,2)");

            migrationBuilder.AlterColumn<decimal>(
                name: "recargo_urgencia",
                table: "pedidos",
                type: "numeric(12,2)",
                nullable: true,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "numeric(12,2)",
                oldDefaultValue: 0m);

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "precio_congelado_en",
                table: "pedidos",
                type: "timestamp with time zone",
                nullable: true,
                defaultValueSql: "now()",
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldDefaultValueSql: "now()");

            migrationBuilder.AlterColumn<decimal>(
                name: "precio_base",
                table: "pedidos",
                type: "numeric(12,2)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(12,2)");

            migrationBuilder.AlterColumn<decimal>(
                name: "descuento_ruta",
                table: "pedidos",
                type: "numeric(12,2)",
                nullable: true,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "numeric(12,2)",
                oldDefaultValue: 0m);

            migrationBuilder.AddCheckConstraint(
                name: "ck_vehiculos_tipo",
                table: "vehiculos",
                sql: "tipo in ('camioneta','moto')");

            migrationBuilder.CreateIndex(
                name: "IX_tarifas_cliente_id_zona_id_tipo_vehiculo",
                table: "tarifas",
                columns: new[] { "cliente_id", "zona_id", "tipo_vehiculo" },
                filter: "vigente_hasta is null");

            migrationBuilder.AddCheckConstraint(
                name: "ck_tarifas_tipo_vehiculo",
                table: "tarifas",
                sql: "tipo_vehiculo in ('camioneta','moto')");

            // ux_tarifas_vigencia es un índice de expresión creado a mano (Migrations/
            // 20260828170859_ReglasDeBaseDeDatos.cs), fuera del modelo de EF — hay que
            // recrearlo acá para que sume tipo_vehiculo (acta changelog 3.11).
            migrationBuilder.Sql("drop index ux_tarifas_vigencia;");
            migrationBuilder.Sql(
                "create unique index ux_tarifas_vigencia on tarifas (coalesce(cliente_id, 0), zona_id, tipo_vehiculo, vigente_desde);");

            // tarifa_vigente() gana un cuarto parámetro: p_tipo_vehiculo.
            migrationBuilder.Sql("""
                create or replace function tarifa_vigente(
                  p_cliente int, p_zona int, p_fecha date, p_tipo_vehiculo text
                ) returns numeric language sql stable as $$
                  select precio from tarifas
                  where zona_id = p_zona
                    and tipo_vehiculo = p_tipo_vehiculo
                    and (cliente_id = p_cliente or cliente_id is null)
                    and vigente_desde <= p_fecha
                    and (vigente_hasta is null or vigente_hasta >= p_fecha)
                  order by cliente_id nulls last, vigente_desde desc
                  limit 1
                $$;
                """);
            migrationBuilder.Sql("drop function if exists tarifa_vigente(int, int, date);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                create or replace function tarifa_vigente(
                  p_cliente int, p_zona int, p_fecha date default current_date
                ) returns numeric language sql stable as $$
                  select precio from tarifas
                  where zona_id = p_zona
                    and (cliente_id = p_cliente or cliente_id is null)
                    and vigente_desde <= p_fecha
                    and (vigente_hasta is null or vigente_hasta >= p_fecha)
                  order by cliente_id nulls last, vigente_desde desc
                  limit 1
                $$;
                """);
            migrationBuilder.Sql("drop function if exists tarifa_vigente(int, int, date, text);");

            migrationBuilder.Sql("drop index ux_tarifas_vigencia;");
            migrationBuilder.Sql(
                "create unique index ux_tarifas_vigencia on tarifas (coalesce(cliente_id, 0), zona_id, vigente_desde);");

            migrationBuilder.DropCheckConstraint(
                name: "ck_vehiculos_tipo",
                table: "vehiculos");

            migrationBuilder.DropIndex(
                name: "IX_tarifas_cliente_id_zona_id_tipo_vehiculo",
                table: "tarifas");

            migrationBuilder.DropCheckConstraint(
                name: "ck_tarifas_tipo_vehiculo",
                table: "tarifas");

            migrationBuilder.DropColumn(
                name: "tipo",
                table: "vehiculos");

            migrationBuilder.DropColumn(
                name: "tipo_vehiculo",
                table: "tarifas");

            migrationBuilder.AlterColumn<decimal>(
                name: "total",
                table: "pedidos",
                type: "numeric(12,2)",
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "numeric(12,2)",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "recargo_urgencia",
                table: "pedidos",
                type: "numeric(12,2)",
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "numeric(12,2)",
                oldNullable: true,
                oldDefaultValue: 0m);

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "precio_congelado_en",
                table: "pedidos",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "now()",
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldNullable: true,
                oldDefaultValueSql: "now()");

            migrationBuilder.AlterColumn<decimal>(
                name: "precio_base",
                table: "pedidos",
                type: "numeric(12,2)",
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "numeric(12,2)",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "descuento_ruta",
                table: "pedidos",
                type: "numeric(12,2)",
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "numeric(12,2)",
                oldNullable: true,
                oldDefaultValue: 0m);

            migrationBuilder.CreateIndex(
                name: "IX_tarifas_cliente_id_zona_id",
                table: "tarifas",
                columns: new[] { "cliente_id", "zona_id" },
                filter: "vigente_hasta is null");
        }
    }
}
