using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Logistica.Migrations
{
    /// <inheritdoc />
    public partial class IndiceBusquedaPedidos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // La búsqueda de /pedidos por nombre usa ILIKE '%texto%'. Sin índice recorre toda la tabla: con
            // 100.000 pedidos tardaba ~200 ms y con 100 usuarios a la vez bajaba a 29 req/s (p95 4,4 s).
            // Un índice GIN de trigramas la deja en ~5 ms. pg_trgm viene con Postgres (y está disponible en
            // Supabase/Neon/RDS); el índice ocupa ~13 MB cada 100.000 pedidos.
            migrationBuilder.Sql("create extension if not exists pg_trgm;");
            migrationBuilder.Sql(
                "create index if not exists ix_pedidos_destinatario_trgm on pedidos using gin (destinatario_nombre gin_trgm_ops);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("drop index if exists ix_pedidos_destinatario_trgm;");
            // La extensión se deja instalada: otros objetos podrían usarla.
        }
    }
}
