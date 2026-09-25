using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Logistica.Migrations
{
    /// <inheritdoc />
    public partial class AgregarRangos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "descuento_rango",
                table: "pedidos",
                type: "numeric(12,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<short>(
                name: "rango_ajuste",
                table: "clientes",
                type: "smallint",
                nullable: false,
                defaultValue: (short)0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "rango_ajuste_en",
                table: "clientes",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "rango_ajuste_motivo",
                table: "clientes",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "rango_ajuste_por",
                table: "clientes",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "rango_ajuste_vence",
                table: "clientes",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "rango_calculado",
                table: "clientes",
                type: "text",
                nullable: false,
                defaultValue: "sin_rango");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "rango_calculado_en",
                table: "clientes",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "rangos",
                columns: table => new
                {
                    codigo = table.Column<string>(type: "text", nullable: false),
                    nombre = table.Column<string>(type: "text", nullable: false),
                    orden = table.Column<int>(type: "integer", nullable: false),
                    min_envios_trimestre = table.Column<int>(type: "integer", nullable: true),
                    min_facturacion_trimestre = table.Column<decimal>(type: "numeric(14,2)", nullable: true),
                    min_antiguedad_meses = table.Column<int>(type: "integer", nullable: true),
                    min_semanas_activas = table.Column<int>(type: "integer", nullable: true),
                    min_pct_pagos_en_termino = table.Column<decimal>(type: "numeric(5,2)", nullable: true),
                    descuento_pct = table.Column<decimal>(type: "numeric(5,2)", nullable: false, defaultValue: 0m),
                    limite_credito = table.Column<decimal>(type: "numeric(14,2)", nullable: true),
                    prioridad = table.Column<int>(type: "integer", nullable: false, defaultValue: 0)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rangos", x => x.codigo);
                    table.CheckConstraint("ck_rangos_descuento", "descuento_pct between 0 and 100");
                    table.CheckConstraint("ck_rangos_minimos", "(min_envios_trimestre is null or min_envios_trimestre >= 0) and (min_facturacion_trimestre is null or min_facturacion_trimestre >= 0) and (min_antiguedad_meses is null or min_antiguedad_meses >= 0) and (min_semanas_activas is null or min_semanas_activas between 0 and 14) and (limite_credito is null or limite_credito >= 0)");
                    table.CheckConstraint("ck_rangos_pct_pagos", "min_pct_pagos_en_termino is null or min_pct_pagos_en_termino between 0 and 100");
                });

            migrationBuilder.CreateTable(
                name: "cliente_rangos",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    cliente_id = table.Column<int>(type: "integer", nullable: false),
                    rango_anterior = table.Column<string>(type: "text", nullable: true),
                    rango_nuevo = table.Column<string>(type: "text", nullable: false),
                    origen = table.Column<string>(type: "text", nullable: false),
                    trimestre = table.Column<string>(type: "text", nullable: true),
                    criterios = table.Column<string>(type: "jsonb", nullable: true),
                    motivo = table.Column<string>(type: "text", nullable: true),
                    registrado_por = table.Column<Guid>(type: "uuid", nullable: false),
                    registrado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cliente_rangos", x => x.id);
                    table.CheckConstraint("ck_cliente_rangos_origen", "origen in ('recalculo','ajuste')");
                    table.ForeignKey(
                        name: "FK_cliente_rangos_clientes_cliente_id",
                        column: x => x.cliente_id,
                        principalTable: "clientes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_cliente_rangos_rangos_rango_anterior",
                        column: x => x.rango_anterior,
                        principalTable: "rangos",
                        principalColumn: "codigo",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_cliente_rangos_rangos_rango_nuevo",
                        column: x => x.rango_nuevo,
                        principalTable: "rangos",
                        principalColumn: "codigo",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_cliente_rangos_usuarios_registrado_por",
                        column: x => x.registrado_por,
                        principalTable: "usuarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "rangos",
                columns: new[] { "codigo", "limite_credito", "min_antiguedad_meses", "min_envios_trimestre", "min_facturacion_trimestre", "min_pct_pagos_en_termino", "min_semanas_activas", "nombre", "orden", "prioridad" },
                values: new object[,]
                {
                    { "bronce", null, null, null, null, null, null, "Bronce", 1, 1 },
                    { "empresa", null, null, null, null, null, null, "Empresa", 4, 4 },
                    { "oro", null, null, null, null, null, null, "Oro", 3, 3 },
                    { "plata", null, null, null, null, null, null, "Plata", 2, 2 }
                });

            migrationBuilder.InsertData(
                table: "rangos",
                columns: new[] { "codigo", "limite_credito", "min_antiguedad_meses", "min_envios_trimestre", "min_facturacion_trimestre", "min_pct_pagos_en_termino", "min_semanas_activas", "nombre", "orden" },
                values: new object[] { "sin_rango", null, null, null, null, null, null, "Sin rango", 0 });

            migrationBuilder.CreateIndex(
                name: "IX_clientes_rango_ajuste_por",
                table: "clientes",
                column: "rango_ajuste_por");

            migrationBuilder.CreateIndex(
                name: "IX_clientes_rango_calculado",
                table: "clientes",
                column: "rango_calculado");

            migrationBuilder.AddCheckConstraint(
                name: "ck_clientes_rango_ajuste",
                table: "clientes",
                sql: "rango_ajuste = 0 or (rango_ajuste in (-1, 1) and rango_ajuste_motivo is not null and btrim(rango_ajuste_motivo) <> '' and rango_ajuste_vence is not null and rango_ajuste_por is not null)");

            migrationBuilder.CreateIndex(
                name: "IX_cliente_rangos_cliente_id_registrado_en",
                table: "cliente_rangos",
                columns: new[] { "cliente_id", "registrado_en" });

            migrationBuilder.CreateIndex(
                name: "IX_cliente_rangos_rango_anterior",
                table: "cliente_rangos",
                column: "rango_anterior");

            migrationBuilder.CreateIndex(
                name: "IX_cliente_rangos_rango_nuevo",
                table: "cliente_rangos",
                column: "rango_nuevo");

            migrationBuilder.CreateIndex(
                name: "IX_cliente_rangos_registrado_por",
                table: "cliente_rangos",
                column: "registrado_por");

            migrationBuilder.CreateIndex(
                name: "IX_rangos_orden",
                table: "rangos",
                column: "orden",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_clientes_rangos_rango_calculado",
                table: "clientes",
                column: "rango_calculado",
                principalTable: "rangos",
                principalColumn: "codigo",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_clientes_usuarios_rango_ajuste_por",
                table: "clientes",
                column: "rango_ajuste_por",
                principalTable: "usuarios",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            // B3, definición J (acta changelog 4.21): descuento_rango entra al conjunto de columnas que
            // fn_congelar_pedido protege — sin esto sería el único término del precio editable después de
            // confirmar (P1). Mismo cuerpo que AgregarPrecioPorKm más esa línea.
            migrationBuilder.Sql(CongelarPedido(conDescuentoRango: true));

            // Historial de rango: solo inserción, mismo criterio que trg_facturas_inmutable.
            migrationBuilder.Sql("""
                create or replace function fn_cliente_rangos_inmutable()
                returns trigger language plpgsql as $$
                begin
                  raise exception 'cliente_rangos es de solo inserción: el historial de rango de un cliente no se edita ni se borra.';
                end $$;

                create trigger trg_cliente_rangos_inmutable
                  before update or delete on cliente_rangos
                  for each row execute function fn_cliente_rangos_inmutable();
                """);
        }

        private static string CongelarPedido(bool conDescuentoRango) => $$"""
            create or replace function fn_congelar_pedido()
            returns trigger language plpgsql as $$
            begin
              if old.estado <> 'borrador' and (
                   new.precio_base      is distinct from old.precio_base
                or new.recargo_urgencia is distinct from old.recargo_urgencia
                or new.descuento_ruta   is distinct from old.descuento_ruta
                {{(conDescuentoRango ? "or new.descuento_rango  is distinct from old.descuento_rango" : "")}}
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
            """;

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                drop trigger if exists trg_cliente_rangos_inmutable on cliente_rangos;
                drop function if exists fn_cliente_rangos_inmutable();
                """);
            // La versión anterior de la función, sin descuento_rango, antes de borrar la columna.
            migrationBuilder.Sql(CongelarPedido(conDescuentoRango: false));

            migrationBuilder.DropForeignKey(
                name: "FK_clientes_rangos_rango_calculado",
                table: "clientes");

            migrationBuilder.DropForeignKey(
                name: "FK_clientes_usuarios_rango_ajuste_por",
                table: "clientes");

            migrationBuilder.DropTable(
                name: "cliente_rangos");

            migrationBuilder.DropTable(
                name: "rangos");

            migrationBuilder.DropIndex(
                name: "IX_clientes_rango_ajuste_por",
                table: "clientes");

            migrationBuilder.DropIndex(
                name: "IX_clientes_rango_calculado",
                table: "clientes");

            migrationBuilder.DropCheckConstraint(
                name: "ck_clientes_rango_ajuste",
                table: "clientes");

            migrationBuilder.DropColumn(
                name: "descuento_rango",
                table: "pedidos");

            migrationBuilder.DropColumn(
                name: "rango_ajuste",
                table: "clientes");

            migrationBuilder.DropColumn(
                name: "rango_ajuste_en",
                table: "clientes");

            migrationBuilder.DropColumn(
                name: "rango_ajuste_motivo",
                table: "clientes");

            migrationBuilder.DropColumn(
                name: "rango_ajuste_por",
                table: "clientes");

            migrationBuilder.DropColumn(
                name: "rango_ajuste_vence",
                table: "clientes");

            migrationBuilder.DropColumn(
                name: "rango_calculado",
                table: "clientes");

            migrationBuilder.DropColumn(
                name: "rango_calculado_en",
                table: "clientes");
        }
    }
}
