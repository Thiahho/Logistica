using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Logistica.Migrations
{
    /// <inheritdoc />
    public partial class ReglasDeBaseDeDatos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ============ ÍNDICES Y CONSTRAINTS DE EXPRESIÓN (EF no los expresa) ============

            migrationBuilder.Sql(
                "create unique index ux_ubicaciones_calle_localidad " +
                "on ubicaciones (lower(calle_numero), localidad_id);");

            migrationBuilder.Sql(
                "create unique index ux_tarifas_vigencia " +
                "on tarifas (coalesce(cliente_id, 0), zona_id, vigente_desde);");

            // DEFERRABLE a propósito: sin eso, el reordenamiento de paradas por drag & drop
            // (RF-12) choca a mitad del UPDATE contra el unique (ruta_id, orden).
            migrationBuilder.Sql(
                "alter table ruta_paradas add constraint parada_orden_uk " +
                "unique (ruta_id, orden) deferrable initially deferred;");

            // ============ FUNCIONES DE DOMINIO ============

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

            migrationBuilder.Sql(@"
create or replace function tarifa_vigente(
  p_cliente int, p_zona int, p_fecha date default current_date
) returns numeric language sql stable as $$
  select precio from tarifas
  where zona_id = p_zona
    and (cliente_id = p_cliente or cliente_id is null)
    and vigente_desde <= p_fecha
    and (vigente_hasta is null or vigente_hasta >= p_fecha)
  order by cliente_id nulls last, vigente_desde desc
  limit 1
$$;");

            // ============ TRIGGERS (RNF-04: las reglas viven en la base, no en la app) ============

            // 1. Log automático de toda transición de estado (P2 / RF-28).
            // El actor viene de app.usuario_id, publicado por el backend en la misma transacción
            // que la escritura (no hay auth.uid(): no hay Supabase). Ausente => actor 'sistema'.
            migrationBuilder.Sql(@"
create or replace function fn_log_estado_pedido()
returns trigger language plpgsql security definer set search_path = public as $$
declare v_actor uuid := nullif(current_setting('app.usuario_id', true), '')::uuid;
begin
  if tg_op = 'INSERT' or new.estado is distinct from old.estado then
    insert into pedido_eventos (
      pedido_id, estado_anterior, estado_nuevo, motivo,
      actor_tipo, actor_usuario_id, actor_texto
    ) values (
      new.id,
      case when tg_op = 'INSERT' then null else old.estado end,
      new.estado,
      nullif(current_setting('app.motivo', true), ''),
      case when v_actor is null then 'sistema' else 'usuario' end,
      v_actor,
      case when v_actor is null then coalesce(
        nullif(current_setting('app.proceso', true), ''), 'automatico') end
    );
  end if;
  return new;
end $$;");

            migrationBuilder.Sql(
                "create trigger trg_log_estado_pedido " +
                "after insert or update on pedidos " +
                "for each row execute function fn_log_estado_pedido();");

            // 2. El log es inmutable (RF-28).
            migrationBuilder.Sql(@"
create or replace function fn_log_inmutable()
returns trigger language plpgsql as $$
begin
  raise exception 'pedido_eventos es de solo inserción';
end $$;");

            migrationBuilder.Sql(
                "create trigger trg_log_inmutable " +
                "before update or delete on pedido_eventos " +
                "for each row execute function fn_log_inmutable();");

            // 3. El precio y el destino se congelan al confirmar (P1 / RF-02 / criterio 7).
            migrationBuilder.Sql(@"
create or replace function fn_congelar_pedido()
returns trigger language plpgsql as $$
begin
  if old.estado <> 'borrador' and (
       new.precio_base      is distinct from old.precio_base
    or new.recargo_urgencia is distinct from old.recargo_urgencia
    or new.descuento_ruta   is distinct from old.descuento_ruta
    or new.peajes           is distinct from old.peajes
    or new.total            is distinct from old.total
    or new.destino_ubicacion_id is distinct from old.destino_ubicacion_id
  ) then
    raise exception
      'Pedido % confirmado: precio y destino no se modifican (P1)', old.id;
  end if;
  return new;
end $$;");

            migrationBuilder.Sql(
                "create trigger trg_congelar_pedido " +
                "before update on pedidos " +
                "for each row execute function fn_congelar_pedido();");

            // 4. Una dirección dudosa no entra a una ruta (RF-05 / criterio 6).
            migrationBuilder.Sql(@"
create or replace function fn_bloquear_direccion_dudosa()
returns trigger language plpgsql as $$
declare v_destino bigint;
begin
  select destino_ubicacion_id into v_destino from pedidos where id = new.pedido_id;
  if not ubicacion_apta(v_destino) then
    raise exception
      'Pedido %: dirección sin geolocalizar, de baja confianza o sin zona. Corregir antes de rutear.',
      new.pedido_id;
  end if;
  return new;
end $$;");

            migrationBuilder.Sql(
                "create trigger trg_bloquear_direccion_dudosa " +
                "before insert on parada_pedidos " +
                "for each row execute function fn_bloquear_direccion_dudosa();");

            // ============ VISTA PARA LA PWA (RNF-08 / construccion_v1.md §3 regla 4) ============
            // Todo lo que el repartidor necesita, ningún importe. Sin security_invoker: no hay
            // RLS que activar, la autorización de qué ruta puede ver cada repartidor vive en el
            // backend (endpoint /api/mis-paradas).

            migrationBuilder.Sql(@"
create or replace view v_paradas_repartidor as
select rp.id as parada_id, rp.ruta_id, rp.orden, rp.tipo, rp.estado,
       rp.llegada_en, rp.salida_en,
       p.id as pedido_id, p.destinatario_nombre, p.destinatario_telefono,
       p.bultos, p.observaciones,
       u.calle_numero, u.referencia, u.lat, u.lng, l.nombre as localidad
from ruta_paradas rp
join parada_pedidos pp on pp.parada_id = rp.id
join pedidos p         on p.id = pp.pedido_id
join ubicaciones u     on u.id = rp.ubicacion_id
left join localidades l on l.id = u.localidad_id;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("drop view if exists v_paradas_repartidor;");

            migrationBuilder.Sql("drop trigger if exists trg_bloquear_direccion_dudosa on parada_pedidos;");
            migrationBuilder.Sql("drop function if exists fn_bloquear_direccion_dudosa();");

            migrationBuilder.Sql("drop trigger if exists trg_congelar_pedido on pedidos;");
            migrationBuilder.Sql("drop function if exists fn_congelar_pedido();");

            migrationBuilder.Sql("drop trigger if exists trg_log_inmutable on pedido_eventos;");
            migrationBuilder.Sql("drop function if exists fn_log_inmutable();");

            migrationBuilder.Sql("drop trigger if exists trg_log_estado_pedido on pedidos;");
            migrationBuilder.Sql("drop function if exists fn_log_estado_pedido();");

            migrationBuilder.Sql("drop function if exists tarifa_vigente(int, int, date);");
            migrationBuilder.Sql("drop function if exists ubicacion_apta(bigint);");

            migrationBuilder.Sql("alter table ruta_paradas drop constraint if exists parada_orden_uk;");
            migrationBuilder.Sql("drop index if exists ux_tarifas_vigencia;");
            migrationBuilder.Sql("drop index if exists ux_ubicaciones_calle_localidad;");
        }
    }
}
