using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Logistica.Migrations
{
    /// <inheritdoc />
    public partial class ConcurrenciaOptimistaDePedidos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Vacía a propósito. Pedido.Version se mapea a `xmin`, una columna de sistema que toda
            // tabla de Postgres ya tiene: no hay nada que crear. EF había generado un
            // AddColumn("xmin") que Npgsql descarta al armar el SQL; se saca para que la migración
            // diga lo que hace. Existe solo para dejar el cambio de modelo en el snapshot.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
