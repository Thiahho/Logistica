using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Logistica.Migrations
{
    /// <inheritdoc />
    public partial class AgregarNovedadUrgencia : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_novedades_origen_coherente",
                table: "novedades");

            migrationBuilder.DropCheckConstraint(
                name: "ck_novedades_tipo",
                table: "novedades");

            migrationBuilder.AddCheckConstraint(
                name: "ck_novedades_origen_coherente",
                table: "novedades",
                sql: "(origen = 'repartidor' and tipo in ('incidencia_ruta','problema_carga','cambio_propuesto')) or (origen = 'operacion' and tipo in ('cambio_operacion','cancelacion','urgencia'))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_novedades_tipo",
                table: "novedades",
                sql: "tipo in ('incidencia_ruta','problema_carga','cambio_propuesto','cambio_operacion','cancelacion','urgencia')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_novedades_origen_coherente",
                table: "novedades");

            migrationBuilder.DropCheckConstraint(
                name: "ck_novedades_tipo",
                table: "novedades");

            migrationBuilder.AddCheckConstraint(
                name: "ck_novedades_origen_coherente",
                table: "novedades",
                sql: "(origen = 'repartidor' and tipo in ('incidencia_ruta','problema_carga','cambio_propuesto')) or (origen = 'operacion' and tipo in ('cambio_operacion','cancelacion'))");

            migrationBuilder.AddCheckConstraint(
                name: "ck_novedades_tipo",
                table: "novedades",
                sql: "tipo in ('incidencia_ruta','problema_carga','cambio_propuesto','cambio_operacion','cancelacion')");
        }
    }
}
