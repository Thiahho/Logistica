using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Logistica.Migrations
{
    /// <inheritdoc />
    public partial class CorregirUbicacionAptaSinLocalidad : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ubicacion_apta hacía inner join con localidades: para una ubicación sin localidad (la
            // columna es nullable) no devolvía fila, o sea null, y `if not null` en
            // fn_bloquear_direccion_dudosa no levanta el error — una dirección sin verificar entraba a
            // una ruta. Con left join y coalesce la función siempre responde true o false; lo mismo
            // para un id que no existe.
            migrationBuilder.Sql(@"
create or replace function ubicacion_apta(p_ubicacion bigint)
returns boolean language sql stable as $$
  select coalesce((
    select u.verificada
        or (coalesce(u.geo_confianza,'fallida') in ('alta','media')
            and l.zona_id is not null)
    from ubicaciones u
    left join localidades l on l.id = u.localidad_id
    where u.id = p_ubicacion
  ), false)
$$;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
create or replace function ubicacion_apta(p_ubicacion bigint)
returns boolean language sql stable as $$
  select u.verificada
      or (coalesce(u.geo_confianza,'fallida') in ('alta','media')
          and l.zona_id is not null)
  from ubicaciones u
  join localidades l on l.id = u.localidad_id
  where u.id = p_ubicacion
$$;");
        }
    }
}
