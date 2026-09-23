using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Logistica.Migrations
{
    /// <inheritdoc />
    public partial class AgregarVehiculos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Orden distinto al que scaffoldea EF a propósito: EF genera Drop(vehiculo) antes de
            // crear la tabla nueva, lo que pierde los datos existentes. Acá se crea vehiculos,
            // se agrega vehiculo_id, se migra el texto libre a filas de vehiculos con SQL crudo,
            // y recién ahí se dropea la columna vieja.
            migrationBuilder.CreateTable(
                name: "vehiculos",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    patente = table.Column<string>(type: "text", nullable: false),
                    descripcion = table.Column<string>(type: "text", nullable: true),
                    marca = table.Column<string>(type: "text", nullable: true),
                    modelo = table.Column<string>(type: "text", nullable: true),
                    anio = table.Column<int>(type: "integer", nullable: true),
                    km_actual = table.Column<int>(type: "integer", nullable: true),
                    vence_vtv = table.Column<DateOnly>(type: "date", nullable: true),
                    vence_seguro = table.Column<DateOnly>(type: "date", nullable: true),
                    costo_km = table.Column<decimal>(type: "numeric(12,2)", nullable: true),
                    capacidad_paradas = table.Column<int>(type: "integer", nullable: false, defaultValue: 24),
                    activo = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_vehiculos", x => x.id);
                    table.CheckConstraint("ck_vehiculos_anio", "anio is null or anio between 1950 and 2100");
                    table.CheckConstraint("ck_vehiculos_capacidad", "capacidad_paradas > 0");
                });

            migrationBuilder.CreateIndex(
                name: "IX_vehiculos_patente",
                table: "vehiculos",
                column: "patente",
                unique: true);

            migrationBuilder.AddColumn<long>(
                name: "vehiculo_id",
                table: "rutas",
                type: "bigint",
                nullable: true);

            // Cada texto libre distinto pasa a ser una fila de vehiculos. La patente queda con el
            // texto original ("Utilitario 1"): administración la corrige desde /vehiculos.
            migrationBuilder.Sql(
                "insert into vehiculos (patente, descripcion) " +
                "select distinct upper(trim(vehiculo)), trim(vehiculo) " +
                "from rutas " +
                "where vehiculo is not null and trim(vehiculo) <> '';");

            migrationBuilder.Sql(
                "update rutas r set vehiculo_id = v.id " +
                "from vehiculos v " +
                "where upper(trim(r.vehiculo)) = v.patente;");

            migrationBuilder.DropColumn(
                name: "vehiculo",
                table: "rutas");

            migrationBuilder.CreateIndex(
                name: "IX_rutas_vehiculo_id",
                table: "rutas",
                column: "vehiculo_id");

            migrationBuilder.AddForeignKey(
                name: "FK_rutas_vehiculos_vehiculo_id",
                table: "rutas",
                column: "vehiculo_id",
                principalTable: "vehiculos",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_rutas_vehiculos_vehiculo_id",
                table: "rutas");

            migrationBuilder.AddColumn<string>(
                name: "vehiculo",
                table: "rutas",
                type: "text",
                nullable: true);

            migrationBuilder.Sql(
                "update rutas r set vehiculo = v.patente " +
                "from vehiculos v " +
                "where v.id = r.vehiculo_id;");

            migrationBuilder.DropIndex(
                name: "IX_rutas_vehiculo_id",
                table: "rutas");

            migrationBuilder.DropColumn(
                name: "vehiculo_id",
                table: "rutas");

            migrationBuilder.DropTable(
                name: "vehiculos");
        }
    }
}
