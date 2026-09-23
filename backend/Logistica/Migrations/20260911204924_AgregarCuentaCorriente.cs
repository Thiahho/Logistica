using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Logistica.Migrations
{
    /// <summary>
    /// E1 (Anexo I §5, B1). Tres tablas nuevas — facturas, factura_items, pagos — reservadas en
    /// el techo de 17 desde E0 (construccion_v1.md §1, changelog 1.17).
    ///
    /// Orden deliberado dentro de Up(): (1) columnas nuevas de clientes; (2) CreateTable de las
    /// tres tablas (autogenerado por EF, en el orden facturas/pagos/factura_items); (3) índices,
    /// checks y FKs (autogenerado); (4) los dos triggers de inmutabilidad — fn_log_inmutable no
    /// se reusa porque tiene el nombre de tabla hardcodeado en el RAISE; (5) la vista
    /// v_facturas_saldo, que necesita facturas y pagos ya creadas; (6) saldo_cliente y
    /// deuda_vencida_cliente, que necesitan la vista ya creada. Invertir (5) y (6) falla con
    /// "relation v_facturas_saldo does not exist".
    ///
    /// Down() revierte el shape en orden inverso; como el resto de las migraciones de este
    /// proyecto, no es un backup — no restaura facturas ni pagos ya escritos.
    /// </summary>
    public partial class AgregarCuentaCorriente : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ciclo_facturacion",
                table: "clientes",
                type: "text",
                nullable: false,
                defaultValue: "mensual");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "corte_suspendido_en",
                table: "clientes",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "corte_suspendido_hasta",
                table: "clientes",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "corte_suspendido_motivo",
                table: "clientes",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "corte_suspendido_por",
                table: "clientes",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "facturas",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    cliente_id = table.Column<int>(type: "integer", nullable: false),
                    ciclo = table.Column<string>(type: "text", nullable: false),
                    periodo_desde = table.Column<DateOnly>(type: "date", nullable: false),
                    periodo_hasta = table.Column<DateOnly>(type: "date", nullable: false),
                    fecha_emision = table.Column<DateOnly>(type: "date", nullable: false),
                    fecha_vencimiento = table.Column<DateOnly>(type: "date", nullable: false),
                    total = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    emitida_por = table.Column<Guid>(type: "uuid", nullable: true),
                    creada_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_facturas", x => x.id);
                    table.CheckConstraint("ck_facturas_ciclo", "ciclo in ('quincenal','mensual')");
                    table.CheckConstraint("ck_facturas_periodo", "periodo_hasta >= periodo_desde");
                    table.CheckConstraint("ck_facturas_vencimiento", "fecha_vencimiento >= periodo_hasta");
                    table.ForeignKey(
                        name: "FK_facturas_clientes_cliente_id",
                        column: x => x.cliente_id,
                        principalTable: "clientes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_facturas_usuarios_emitida_por",
                        column: x => x.emitida_por,
                        principalTable: "usuarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "pagos",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    cliente_id = table.Column<int>(type: "integer", nullable: false),
                    monto = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    fecha_pago = table.Column<DateOnly>(type: "date", nullable: false, defaultValueSql: "current_date"),
                    medio = table.Column<string>(type: "text", nullable: false),
                    nota = table.Column<string>(type: "text", nullable: true),
                    registrado_por = table.Column<Guid>(type: "uuid", nullable: true),
                    registrado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pagos", x => x.id);
                    table.CheckConstraint("ck_pagos_medio", "medio in ('transferencia','efectivo','cheque','otro')");
                    table.CheckConstraint("ck_pagos_monto", "monto <> 0");
                    table.CheckConstraint("ck_pagos_reverso_nota", "monto > 0 or (nota is not null and length(btrim(nota)) > 0)");
                    table.ForeignKey(
                        name: "FK_pagos_clientes_cliente_id",
                        column: x => x.cliente_id,
                        principalTable: "clientes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_pagos_usuarios_registrado_por",
                        column: x => x.registrado_por,
                        principalTable: "usuarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "factura_items",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    factura_id = table.Column<long>(type: "bigint", nullable: true),
                    pedido_id = table.Column<long>(type: "bigint", nullable: true),
                    tipo = table.Column<string>(type: "text", nullable: false),
                    descripcion = table.Column<string>(type: "text", nullable: false),
                    monto = table.Column<decimal>(type: "numeric(12,2)", nullable: true),
                    estado = table.Column<string>(type: "text", nullable: false, defaultValue: "aprobado"),
                    creado_por = table.Column<Guid>(type: "uuid", nullable: true),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    resuelto_por = table.Column<Guid>(type: "uuid", nullable: true),
                    resuelto_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_factura_items", x => x.id);
                    table.CheckConstraint("ck_factura_items_estado", "estado in ('pendiente','aprobado','rechazado')");
                    table.CheckConstraint("ck_factura_items_estado_factura", "estado = 'aprobado' or factura_id is null");
                    table.CheckConstraint("ck_factura_items_monto", "estado <> 'aprobado' or monto is not null");
                    table.CheckConstraint("ck_factura_items_pedido", "tipo <> 'pedido' or (pedido_id is not null and estado = 'aprobado')");
                    table.CheckConstraint("ck_factura_items_resolucion", "(estado = 'pendiente' and resuelto_por is null and resuelto_en is null) or (estado <> 'pendiente' and (resuelto_por is null) = (resuelto_en is null))");
                    table.CheckConstraint("ck_factura_items_tipo", "tipo in ('pedido','ajuste','nota_credito')");
                    table.ForeignKey(
                        name: "FK_factura_items_facturas_factura_id",
                        column: x => x.factura_id,
                        principalTable: "facturas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_factura_items_pedidos_pedido_id",
                        column: x => x.pedido_id,
                        principalTable: "pedidos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_factura_items_usuarios_creado_por",
                        column: x => x.creado_por,
                        principalTable: "usuarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_factura_items_usuarios_resuelto_por",
                        column: x => x.resuelto_por,
                        principalTable: "usuarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_clientes_corte_suspendido_por",
                table: "clientes",
                column: "corte_suspendido_por");

            migrationBuilder.AddCheckConstraint(
                name: "ck_clientes_ciclo_facturacion",
                table: "clientes",
                sql: "ciclo_facturacion in ('quincenal','mensual')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_clientes_corte_suspendido",
                table: "clientes",
                sql: "(corte_suspendido_hasta is null and corte_suspendido_por is null) or (corte_suspendido_hasta is not null and corte_suspendido_por is not null and corte_suspendido_motivo is not null)");

            migrationBuilder.CreateIndex(
                name: "IX_factura_items_creado_por",
                table: "factura_items",
                column: "creado_por");

            migrationBuilder.CreateIndex(
                name: "IX_factura_items_estado_factura_id",
                table: "factura_items",
                columns: new[] { "estado", "factura_id" },
                filter: "factura_id is null");

            migrationBuilder.CreateIndex(
                name: "IX_factura_items_factura_id",
                table: "factura_items",
                column: "factura_id");

            migrationBuilder.CreateIndex(
                name: "IX_factura_items_resuelto_por",
                table: "factura_items",
                column: "resuelto_por");

            migrationBuilder.CreateIndex(
                name: "ux_factura_items_pedido",
                table: "factura_items",
                column: "pedido_id",
                unique: true,
                filter: "tipo = 'pedido'");

            migrationBuilder.CreateIndex(
                name: "IX_facturas_cliente_id_fecha_emision",
                table: "facturas",
                columns: new[] { "cliente_id", "fecha_emision" });

            migrationBuilder.CreateIndex(
                name: "IX_facturas_emitida_por",
                table: "facturas",
                column: "emitida_por");

            migrationBuilder.CreateIndex(
                name: "IX_facturas_fecha_vencimiento",
                table: "facturas",
                column: "fecha_vencimiento");

            migrationBuilder.CreateIndex(
                name: "ux_facturas_periodo",
                table: "facturas",
                columns: new[] { "cliente_id", "periodo_hasta" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_pagos_cliente_id_fecha_pago",
                table: "pagos",
                columns: new[] { "cliente_id", "fecha_pago" });

            migrationBuilder.CreateIndex(
                name: "IX_pagos_registrado_por",
                table: "pagos",
                column: "registrado_por");

            migrationBuilder.AddForeignKey(
                name: "FK_clientes_usuarios_corte_suspendido_por",
                table: "clientes",
                column: "corte_suspendido_por",
                principalTable: "usuarios",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            // Inmutabilidad de facturas y pagos: espíritu de trg_congelar_pedido (P1) y
            // trg_log_inmutable (P2), pero con funciones propias — fn_log_inmutable tiene el
            // nombre de "pedido_eventos" hardcodeado en el RAISE, reusarla acá daría un mensaje
            // engañoso.
            migrationBuilder.Sql("""
                create or replace function fn_facturas_inmutable()
                returns trigger language plpgsql as $$
                begin
                  raise exception 'Factura %: una factura emitida no se edita ni se borra. Las correcciones son items nuevos que entran en la siguiente factura.',
                    coalesce(old.id, new.id);
                end $$;
                """);
            migrationBuilder.Sql("""
                create trigger trg_facturas_inmutable
                  before update or delete on facturas
                  for each row execute function fn_facturas_inmutable();
                """);

            migrationBuilder.Sql("""
                create or replace function fn_pagos_inmutable()
                returns trigger language plpgsql as $$
                begin
                  raise exception 'pagos es de solo inserción: un pago mal cargado se corrige con un pago de monto negativo y nota, no editando el original.';
                end $$;
                """);
            migrationBuilder.Sql("""
                create trigger trg_pagos_inmutable
                  before update or delete on pagos
                  for each row execute function fn_pagos_inmutable();
                """);

            // El FIFO de imputación de pagos (acta §10.2-B/D12), en un solo lugar: cuánto se
            // acumuló en facturas hasta e incluyendo cada una (por fecha de emisión), menos lo
            // pagado en total por el cliente, acotado a [0, total] de esa factura. Una factura
            // con total negativo (crédito neto del período) reduce el acumulado de las
            // siguientes sin romper el clamp.
            migrationBuilder.Sql("""
                create or replace view v_facturas_saldo as
                with pagos_cliente as (
                  select cliente_id, coalesce(sum(monto), 0) as pagado from pagos group by cliente_id
                ),
                acum as (
                  select f.*,
                         sum(f.total) over (partition by f.cliente_id
                                             order by f.fecha_emision, f.id
                                             rows between unbounded preceding and current row) as acumulado
                  from facturas f
                )
                select a.id, a.cliente_id, a.ciclo, a.periodo_desde, a.periodo_hasta,
                       a.fecha_emision, a.fecha_vencimiento, a.total,
                       greatest(0::numeric, least(a.total, a.acumulado - coalesce(p.pagado, 0)))          as saldo,
                       a.total - greatest(0::numeric, least(a.total, a.acumulado - coalesce(p.pagado,0))) as pagado
                from acum a
                left join pagos_cliente p on p.cliente_id = a.cliente_id;
                """);

            // saldo_cliente: para mostrar en pantalla, incluye deuda todavía no vencida.
            // deuda_vencida_cliente: la que realmente gatea el corte de servicio (§10.2-L1/L3) —
            // se apoya en v_facturas_saldo, no reimplementa el FIFO.
            migrationBuilder.Sql("""
                create or replace function saldo_cliente(p_cliente int) returns numeric language sql stable as $$
                  select coalesce((select sum(total) from facturas where cliente_id = p_cliente), 0)
                       - coalesce((select sum(monto)  from pagos    where cliente_id = p_cliente), 0)
                $$;
                """);
            migrationBuilder.Sql("""
                create or replace function deuda_vencida_cliente(p_cliente int, p_fecha date default current_date)
                returns numeric language sql stable as $$
                  select coalesce(sum(saldo), 0) from v_facturas_saldo
                  where cliente_id = p_cliente and fecha_vencimiento < p_fecha
                $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("drop function if exists deuda_vencida_cliente(int, date);");
            migrationBuilder.Sql("drop function if exists saldo_cliente(int);");
            migrationBuilder.Sql("drop view if exists v_facturas_saldo;");
            migrationBuilder.Sql("drop trigger if exists trg_pagos_inmutable on pagos;");
            migrationBuilder.Sql("drop function if exists fn_pagos_inmutable();");
            migrationBuilder.Sql("drop trigger if exists trg_facturas_inmutable on facturas;");
            migrationBuilder.Sql("drop function if exists fn_facturas_inmutable();");

            migrationBuilder.DropForeignKey(
                name: "FK_clientes_usuarios_corte_suspendido_por",
                table: "clientes");

            migrationBuilder.DropTable(
                name: "factura_items");

            migrationBuilder.DropTable(
                name: "pagos");

            migrationBuilder.DropTable(
                name: "facturas");

            migrationBuilder.DropIndex(
                name: "IX_clientes_corte_suspendido_por",
                table: "clientes");

            migrationBuilder.DropCheckConstraint(
                name: "ck_clientes_ciclo_facturacion",
                table: "clientes");

            migrationBuilder.DropCheckConstraint(
                name: "ck_clientes_corte_suspendido",
                table: "clientes");

            migrationBuilder.DropColumn(
                name: "ciclo_facturacion",
                table: "clientes");

            migrationBuilder.DropColumn(
                name: "corte_suspendido_en",
                table: "clientes");

            migrationBuilder.DropColumn(
                name: "corte_suspendido_hasta",
                table: "clientes");

            migrationBuilder.DropColumn(
                name: "corte_suspendido_motivo",
                table: "clientes");

            migrationBuilder.DropColumn(
                name: "corte_suspendido_por",
                table: "clientes");
        }
    }
}
