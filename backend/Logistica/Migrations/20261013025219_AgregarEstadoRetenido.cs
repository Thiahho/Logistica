using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Logistica.Migrations
{
    /// <inheritdoc />
    public partial class AgregarEstadoRetenido : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Genera `alter type estado_pedido add value 'retenido'`. Postgres no deja USAR un valor de
            // enum nuevo dentro de la misma transacción que lo agrega, y cada migración corre en la
            // suya: por eso acá no hay ninguna sentencia que escriba o compare 'retenido'. Lo que
            // necesite el valor (un backfill, una función) va en una migración posterior.
            //
            // Ninguna función ni vista escrita a mano cambia: fn_log_estado_pedido registra cualquier
            // transición, v_paradas_repartidor solo pasa el estado, y fn_congelar_pedido congela todo
            // lo que no es 'borrador' — un retenido no se re-cotiza, que es lo que se quiere.
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:Enum:estado_pedido", "borrador,confirmado,en_ruta,entregado,fallido,reprogramado,devuelto,cancelado,retenido")
                .OldAnnotation("Npgsql:Enum:estado_pedido", "borrador,confirmado,en_ruta,entregado,fallido,reprogramado,devuelto,cancelado");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Vacía a propósito. Postgres no tiene `alter type ... drop value`, y el AlterDatabase
            // inverso que genera EF hace que Npgsql tire al armar el SQL ("Can't remove enum label").
            // El valor queda en el tipo: sin código que lo escriba es inofensivo. Si hay pedidos en
            // 'retenido', volver atrás exige resolverlos antes (reanudarlos o devolverlos).
        }
    }
}
