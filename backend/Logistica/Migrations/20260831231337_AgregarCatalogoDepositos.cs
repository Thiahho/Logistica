using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Logistica.Migrations
{
    /// <inheritdoc />
    public partial class AgregarCatalogoDepositos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "nombre_deposito",
                table: "ubicaciones",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ubicaciones_nombre_deposito",
                table: "ubicaciones",
                column: "nombre_deposito",
                unique: true,
                filter: "nombre_deposito is not null");

            // Data-fix (acta changelog 3.8): promueve el depósito legado (marcado con el string
            // mágico "deposito" en `referencia`, changelog 3.6/3.7) al catálogo nombrado, y
            // backfillea toda ruta con origen_ubicacion_id null a ese id — el fallback implícito
            // "null = el depósito" se retira en esta misma versión (el planificador elige
            // siempre a mano de acá en más), así que ninguna ruta puede quedar sin resolver.
            // En una base recién sembrada no hay ninguna fila con referencia='deposito' todavía
            // (DatosSemilla usa NombreDeposito directo) — estas tres sentencias no afectan nada ahí.
            migrationBuilder.Sql("update ubicaciones set nombre_deposito = 'Depósito' where referencia = 'deposito';");
            migrationBuilder.Sql(@"
                update rutas set origen_ubicacion_id = (select id from ubicaciones where referencia = 'deposito')
                where origen_ubicacion_id is null
                  and exists (select 1 from ubicaciones where referencia = 'deposito');
            ");
            migrationBuilder.Sql("update ubicaciones set referencia = null where referencia = 'deposito';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ubicaciones_nombre_deposito",
                table: "ubicaciones");

            migrationBuilder.DropColumn(
                name: "nombre_deposito",
                table: "ubicaciones");
        }
    }
}
