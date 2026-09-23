using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Logistica.Migrations
{
    /// <inheritdoc />
    public partial class AgregarPrecioManual : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "precio_manual",
                table: "pedidos",
                type: "numeric(12,2)",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "precio_manual_en",
                table: "pedidos",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "precio_manual_por",
                table: "pedidos",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_pedidos_precio_manual_por",
                table: "pedidos",
                column: "precio_manual_por");

            migrationBuilder.AddCheckConstraint(
                name: "ck_pedidos_precio_manual",
                table: "pedidos",
                sql: "precio_manual is null or precio_manual > 0");

            migrationBuilder.AddForeignKey(
                name: "FK_pedidos_usuarios_precio_manual_por",
                table: "pedidos",
                column: "precio_manual_por",
                principalTable: "usuarios",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            // B9 (Anexo I §4): precio_manual entra al conjunto de columnas que
            // fn_congelar_pedido protege (schema_v3.sql §"trg_congelar_pedido") — sin esto sería
            // el único campo de precio editable después de confirmar (P1).
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
                    or new.destino_ubicacion_id is distinct from old.destino_ubicacion_id
                  ) then
                    raise exception
                      'Pedido % confirmado: precio y destino no se modifican (P1)', old.id;
                  end if;
                  return new;
                end $$;
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_pedidos_usuarios_precio_manual_por",
                table: "pedidos");

            migrationBuilder.DropIndex(
                name: "IX_pedidos_precio_manual_por",
                table: "pedidos");

            migrationBuilder.DropCheckConstraint(
                name: "ck_pedidos_precio_manual",
                table: "pedidos");

            migrationBuilder.DropColumn(
                name: "precio_manual",
                table: "pedidos");

            migrationBuilder.DropColumn(
                name: "precio_manual_en",
                table: "pedidos");

            migrationBuilder.DropColumn(
                name: "precio_manual_por",
                table: "pedidos");
        }
    }
}
