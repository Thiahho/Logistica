using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Logistica.Migrations
{
    /// <inheritdoc />
    public partial class AgregarTipoEventoAvisoCobranza : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Panel de cobranza (clientes críticos): tipo NUEVO, no reusar 'impago'. 'impago' es
            // un hecho sobre la CONDUCTA del cliente y va a alimentar el futuro motor de rango
            // (B3, acta §9.2); insertar una fila 'impago' cada vez que un admin aprieta "enviar
            // aviso" ensuciaría el historial de comportamiento con una acción NUESTRA, no del
            // cliente. Cuando se construya B3, aviso_cobranza debe quedar excluido del scoring.
            migrationBuilder.Sql("""
                insert into tipos_evento_cliente (codigo, dimension, descripcion)
                values ('aviso_cobranza', 'pago', 'Aviso de cobranza enviado al cliente')
                on conflict (codigo) do nothing;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Falla con FK violation si ya hay eventos_cliente referenciando este tipo —
            // correcto: no hay que poder borrar el catálogo por debajo de un historial real.
            migrationBuilder.Sql("delete from tipos_evento_cliente where codigo = 'aviso_cobranza';");
        }
    }
}
