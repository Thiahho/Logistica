using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Logistica.Migrations
{
    /// <inheritdoc />
    public partial class AgregarPagosInformados : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "pagos_informados",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    cliente_id = table.Column<int>(type: "integer", nullable: false),
                    cliente_usuario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    monto = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    fecha_pago = table.Column<DateOnly>(type: "date", nullable: false),
                    medio = table.Column<string>(type: "text", nullable: false),
                    nota = table.Column<string>(type: "text", nullable: true),
                    comprobante_path = table.Column<string>(type: "text", nullable: true),
                    estado = table.Column<string>(type: "text", nullable: false, defaultValue: "pendiente"),
                    motivo_rechazo = table.Column<string>(type: "text", nullable: true),
                    revisado_por = table.Column<Guid>(type: "uuid", nullable: true),
                    revisado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    pago_id = table.Column<long>(type: "bigint", nullable: true),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pagos_informados", x => x.id);
                    table.CheckConstraint("ck_pagos_informados_estado", "estado in ('pendiente','confirmado','rechazado')");
                    table.CheckConstraint("ck_pagos_informados_medio", "medio in ('transferencia','efectivo','cheque','otro')");
                    table.CheckConstraint("ck_pagos_informados_monto", "monto > 0");
                    table.CheckConstraint("ck_pagos_informados_pago", "(estado = 'confirmado') = (pago_id is not null)");
                    table.CheckConstraint("ck_pagos_informados_rechazo", "estado <> 'rechazado' or (motivo_rechazo is not null and length(btrim(motivo_rechazo)) > 0)");
                    table.ForeignKey(
                        name: "FK_pagos_informados_clientes_cliente_id",
                        column: x => x.cliente_id,
                        principalTable: "clientes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_pagos_informados_clientes_usuarios_cliente_usuario_id",
                        column: x => x.cliente_usuario_id,
                        principalTable: "clientes_usuarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_pagos_informados_pagos_pago_id",
                        column: x => x.pago_id,
                        principalTable: "pagos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_pagos_informados_usuarios_revisado_por",
                        column: x => x.revisado_por,
                        principalTable: "usuarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_pagos_informados_cliente_id_creado_en",
                table: "pagos_informados",
                columns: new[] { "cliente_id", "creado_en" });

            migrationBuilder.CreateIndex(
                name: "IX_pagos_informados_cliente_usuario_id",
                table: "pagos_informados",
                column: "cliente_usuario_id");

            migrationBuilder.CreateIndex(
                name: "IX_pagos_informados_estado",
                table: "pagos_informados",
                column: "estado");

            migrationBuilder.CreateIndex(
                name: "IX_pagos_informados_pago_id",
                table: "pagos_informados",
                column: "pago_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_pagos_informados_revisado_por",
                table: "pagos_informados",
                column: "revisado_por");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "pagos_informados");
        }
    }
}
