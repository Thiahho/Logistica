using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Logistica.Migrations
{
    /// <inheritdoc />
    public partial class AgregarPrecioPorKm : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_pedidos_origen_coherente",
                table: "pedidos");

            migrationBuilder.DropCheckConstraint(
                name: "ck_pedidos_tipo",
                table: "pedidos");

            migrationBuilder.AddColumn<decimal>(
                name: "km_cobrados",
                table: "pedidos",
                type: "numeric(6,2)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "km_fuente",
                table: "pedidos",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "km_manual",
                table: "pedidos",
                type: "numeric(6,2)",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "km_manual_en",
                table: "pedidos",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "km_manual_por",
                table: "pedidos",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "recargo_km",
                table: "pedidos",
                type: "numeric(12,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateIndex(
                name: "IX_pedidos_km_manual_por",
                table: "pedidos",
                column: "km_manual_por");

            migrationBuilder.AddCheckConstraint(
                name: "ck_pedidos_km_cobrados",
                table: "pedidos",
                sql: "km_cobrados is null or km_cobrados >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_pedidos_km_fuente",
                table: "pedidos",
                sql: "km_fuente is null or km_fuente in ('ruta','recta','manual')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_pedidos_km_manual",
                table: "pedidos",
                sql: "km_manual is null or km_manual >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_pedidos_origen_coherente",
                table: "pedidos",
                sql: "tipo in ('entrega','delivery') or pedido_origen_id is not null");

            migrationBuilder.AddCheckConstraint(
                name: "ck_pedidos_tipo",
                table: "pedidos",
                sql: "tipo in ('entrega','retorno','reintento','delivery')");

            migrationBuilder.AddForeignKey(
                name: "FK_pedidos_usuarios_km_manual_por",
                table: "pedidos",
                column: "km_manual_por",
                principalTable: "usuarios",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            // Anexo I §10.2-N: km_cobrados/recargo_km/km_fuente/km_manual entran al conjunto de
            // columnas que fn_congelar_pedido protege (schema_v3.sql §"trg_congelar_pedido") —
            // sin esto serían las únicas columnas de precio editables después de confirmar (P1).
            migrationBuilder.Sql("""
                create or replace function fn_congelar_pedido()
                returns trigger language plpgsql as $$
                begin
                  if old.estado <> 'borrador' and (
                       new.precio_base      is distinct from old.precio_base
                    or new.recargo_urgencia is distinct from old.recargo_urgencia
                    or new.descuento_ruta   is distinct from old.descuento_ruta
                    or new.peajes           is distinct from old.peajes
                    or new.total            is distinct from old.total
                    or new.precio_manual    is distinct from old.precio_manual
                    or new.km_cobrados      is distinct from old.km_cobrados
                    or new.recargo_km       is distinct from old.recargo_km
                    or new.km_fuente        is distinct from old.km_fuente
                    or new.km_manual        is distinct from old.km_manual
                    or new.destino_ubicacion_id is distinct from old.destino_ubicacion_id
                  ) then
                    raise exception
                      'Pedido % confirmado: precio y destino no se modifican (P1)', old.id;
                  end if;
                  return new;
                end $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                create or replace function fn_congelar_pedido()
                returns trigger language plpgsql as $$
                begin
                  if old.estado <> 'borrador' and (
                       new.precio_base      is distinct from old.precio_base
                    or new.recargo_urgencia is distinct from old.recargo_urgencia
                    or new.descuento_ruta   is distinct from old.descuento_ruta
                    or new.peajes           is distinct from old.peajes
                    or new.total            is distinct from old.total
                    or new.precio_manual    is distinct from old.precio_manual
                    or new.destino_ubicacion_id is distinct from old.destino_ubicacion_id
                  ) then
                    raise exception
                      'Pedido % confirmado: precio y destino no se modifican (P1)', old.id;
                  end if;
                  return new;
                end $$;
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_pedidos_usuarios_km_manual_por",
                table: "pedidos");

            migrationBuilder.DropIndex(
                name: "IX_pedidos_km_manual_por",
                table: "pedidos");

            migrationBuilder.DropCheckConstraint(
                name: "ck_pedidos_km_cobrados",
                table: "pedidos");

            migrationBuilder.DropCheckConstraint(
                name: "ck_pedidos_km_fuente",
                table: "pedidos");

            migrationBuilder.DropCheckConstraint(
                name: "ck_pedidos_km_manual",
                table: "pedidos");

            migrationBuilder.DropCheckConstraint(
                name: "ck_pedidos_origen_coherente",
                table: "pedidos");

            migrationBuilder.DropCheckConstraint(
                name: "ck_pedidos_tipo",
                table: "pedidos");

            migrationBuilder.DropColumn(
                name: "km_cobrados",
                table: "pedidos");

            migrationBuilder.DropColumn(
                name: "km_fuente",
                table: "pedidos");

            migrationBuilder.DropColumn(
                name: "km_manual",
                table: "pedidos");

            migrationBuilder.DropColumn(
                name: "km_manual_en",
                table: "pedidos");

            migrationBuilder.DropColumn(
                name: "km_manual_por",
                table: "pedidos");

            migrationBuilder.DropColumn(
                name: "recargo_km",
                table: "pedidos");

            migrationBuilder.AddCheckConstraint(
                name: "ck_pedidos_origen_coherente",
                table: "pedidos",
                sql: "tipo = 'entrega' or pedido_origen_id is not null");

            migrationBuilder.AddCheckConstraint(
                name: "ck_pedidos_tipo",
                table: "pedidos",
                sql: "tipo in ('entrega','retorno','reintento')");
        }
    }
}
