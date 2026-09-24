using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Logistica.Migrations
{
    /// <inheritdoc />
    public partial class AgregarRentabilidad : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "costos_fijos",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    mes = table.Column<DateOnly>(type: "date", nullable: false),
                    categoria = table.Column<string>(type: "text", nullable: false),
                    descripcion = table.Column<string>(type: "text", nullable: true),
                    monto = table.Column<decimal>(type: "numeric(14,2)", nullable: false),
                    creado_por = table.Column<Guid>(type: "uuid", nullable: false),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_costos_fijos", x => x.id);
                    table.CheckConstraint("ck_costos_fijos_categoria", "btrim(categoria) <> ''");
                    table.CheckConstraint("ck_costos_fijos_mes", "extract(day from mes) = 1");
                    table.CheckConstraint("ck_costos_fijos_monto", "monto >= 0");
                    table.ForeignKey(
                        name: "FK_costos_fijos_usuarios_creado_por",
                        column: x => x.creado_por,
                        principalTable: "usuarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "objetivos_rentabilidad",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    nombre = table.Column<string>(type: "text", nullable: false),
                    pct_min = table.Column<decimal>(type: "numeric(5,2)", nullable: false),
                    pct_max = table.Column<decimal>(type: "numeric(5,2)", nullable: false),
                    fuentes = table.Column<string[]>(type: "text[]", nullable: false),
                    orden = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_objetivos_rentabilidad", x => x.id);
                    table.CheckConstraint("ck_objetivos_rentabilidad_pct", "pct_min >= 0 and pct_max <= 100 and pct_min <= pct_max");
                });

            migrationBuilder.CreateIndex(
                name: "IX_costos_fijos_creado_por",
                table: "costos_fijos",
                column: "creado_por");

            migrationBuilder.CreateIndex(
                name: "IX_costos_fijos_mes",
                table: "costos_fijos",
                column: "mes");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "costos_fijos");

            migrationBuilder.DropTable(
                name: "objetivos_rentabilidad");
        }
    }
}
