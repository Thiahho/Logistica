-- =====================================================================
-- ESQUEMA COMPLETO — Sistema de gestión logística
-- Alineado al Acta v3.0 (27/08/2026). Reemplaza el DDL del MVP.
-- Base vacía: ejecutar entero, de una vez, en Supabase.
--
-- 13 tablas. Ese es el techo hasta el tercer cliente.
-- Excepción registrada (acta §11.1, changelog 3.3): `clientes_usuarios` sube el conteo a 14 —
-- el login de cliente no puede compartir tabla con el personal interno de `usuarios`.
--
-- NOTA (post H0/H1, ver construccion_v1.md §1): el backend real es ASP.NET Core + Postgres en
-- Docker, no Supabase. No hay auth.uid() ni RLS ejecutándose: `usuarios.id` ya no referencia
-- `auth.users`, mi_rol()/mi_cliente() y las policies de esta sección no están aplicadas contra
-- la base real. El control de acceso equivalente vive en las políticas de autorización de
-- Program.cs (BackOffice/Administracion/Operacion/Repartidor/Cliente), y el actor que
-- fn_log_estado_pedido necesita en vez de auth.uid() lo publica EscrituraDominio.GuardarComoAsync
-- como el GUC de sesión `app.usuario_id`. El resto del DDL —tablas, triggers, funciones,
-- vista— sí está aplicado tal cual (ver Migrations/20260828170859_ReglasDeBaseDeDatos.cs).
-- Por el mismo motivo, `usuarios` y `clientes_usuarios` en la base real tienen columnas propias
-- `email` y `password_hash` (auth JWT propio, TokenService/AuthService) en vez de depender de
-- `auth.users` — acá se mantiene el `id references auth.users` original solo como referencia
-- del diseño previo a H0/H1, igual que el resto de esta nota.
-- =====================================================================

-- ============ GEOGRAFÍA ============

create table zonas (
  id        serial primary key,
  codigo    char(1) not null unique,              -- A, B, C, D
  nombre    text not null,
  activa    boolean not null default true,
  km_desde  int check (km_desde is null or km_desde >= 0),
  km_hasta  int check (km_hasta is null or km_desde is null or km_hasta > km_desde)  -- null = sin límite superior
);
-- El precio NO vive acá. Vive en `tarifas` (RF-09).
-- km_desde/km_hasta tampoco traen valor de fábrica: son datos comerciales que se cargan desde
-- /tarifas, mismo criterio que el precio (acta_sistema.md §13).

create table localidades (
  id       serial primary key,
  nombre   text not null,
  partido  text,
  cp       text,
  zona_id  int references zonas(id),            -- null = no zonificable (RF-05)
  unique (nombre, partido)
);

create table ubicaciones (
  id               bigserial primary key,
  calle_numero     text not null,
  localidad_id     int references localidades(id),
  referencia       text,
  nombre_deposito  text,  -- no-nulo = depósito del catálogo, con este nombre (acta 3.8)
  lat              numeric(10,7),
  lng              numeric(10,7),
  geo_confianza    text check (geo_confianza in ('alta','media','baja','fallida')),
  geo_proveedor    text,
  geo_fecha        timestamptz,
  verificada       boolean not null default false,   -- corregida a mano
  creada_en        timestamptz not null default now()
);
create index on ubicaciones (localidad_id);
create unique index on ubicaciones (lower(calle_numero), localidad_id);
create unique index on ubicaciones (nombre_deposito) where nombre_deposito is not null;

-- Una ubicación es apta para ruta si está geolocalizada con confianza
-- o fue verificada a mano, y su localidad tiene zona (RF-05).
create or replace function ubicacion_apta(p_ubicacion bigint)
returns boolean language sql stable as $$
  select u.verificada
      or (coalesce(u.geo_confianza,'fallida') in ('alta','media')
          and l.zona_id is not null)
  from ubicaciones u
  join localidades l on l.id = u.localidad_id
  where u.id = p_ubicacion
$$;

-- ============ COMERCIAL ============

create table clientes (
  id            serial primary key,
  razon_social  text not null,
  cuit          text,
  contacto      text,
  telefono      text,
  email         text,
  color_pago    text not null default 'rojo'
                check (color_pago in ('verde','amarillo','rojo')),
  color_trato   text not null default 'amarillo'
                check (color_trato in ('verde','amarillo','rojo')),
  color_oper    text not null default 'amarillo'
                check (color_oper in ('verde','amarillo','rojo')),
  activo        boolean not null default true,
  creado_en     timestamptz not null default now()
);
-- Los tres colores son internos. Nunca se exponen al rol 'cliente' (RF-33).

-- RF-09: precio por cliente y zona, con lista general como default.
create table tarifas (
  id             bigserial primary key,
  cliente_id     int references clientes(id),   -- null = lista general
  zona_id        int not null references zonas(id),
  tipo_vehiculo  text not null default 'camioneta'
                 check (tipo_vehiculo in ('camioneta','moto')),  -- acta changelog 3.11
  precio         numeric(12,2) not null check (precio > 0),
  vigente_desde  date not null default current_date,
  vigente_hasta  date,
  creada_en      timestamptz not null default now(),
  check (vigente_hasta is null or vigente_hasta >= vigente_desde)
);
create unique index ux_tarifas_vigencia
  on tarifas (coalesce(cliente_id, 0), zona_id, tipo_vehiculo, vigente_desde);
create index on tarifas (cliente_id, zona_id, tipo_vehiculo) where vigente_hasta is null;

-- Resolución: tarifa del cliente si existe, si no la general. p_tipo_vehiculo desde acta
-- changelog 3.11 — camioneta y moto tienen tarifa propia, no una es un factor de la otra.
create or replace function tarifa_vigente(
  p_cliente int, p_zona int, p_fecha date, p_tipo_vehiculo text
) returns numeric language sql stable as $$
  select precio from tarifas
  where zona_id = p_zona
    and tipo_vehiculo = p_tipo_vehiculo
    and (cliente_id = p_cliente or cliente_id is null)
    and vigente_desde <= p_fecha
    and (vigente_hasta is null or vigente_hasta >= p_fecha)
  order by cliente_id nulls last, vigente_desde desc
  limit 1
$$;

-- ============ USUARIOS Y ROLES (11.1 / RNF-08) ============

-- Personal interno únicamente. El login de cliente es otra tabla (ver clientes_usuarios más
-- abajo) a propósito: los dos tipos de cuenta no comparten gestión ni ciclo de vida.
create table usuarios (
  id          uuid primary key references auth.users(id) on delete cascade,
  nombre      text not null,
  rol         text not null
              check (rol in ('administracion','operacion','repartidor')),
  activo      boolean not null default true,
  creado_en   timestamptz not null default now()
);

-- Login de consulta de una empresa cliente. Separada de `usuarios` para que un cliente no
-- pueda convivir con el ABM del personal interno ni compartir su tabla.
create table clientes_usuarios (
  id          uuid primary key references auth.users(id) on delete cascade,
  cliente_id  int not null references clientes(id),
  nombre      text not null,
  email       text not null unique,
  activo      boolean not null default true,
  creado_en   timestamptz not null default now()
);

create or replace function mi_rol() returns text
language sql stable security definer set search_path = public as $$
  select rol from usuarios where id = auth.uid() and activo
$$;

-- Devuelve el cliente si quien está logueado es un login de cliente; null si es personal interno.
create or replace function mi_cliente() returns int
language sql stable security definer set search_path = public as $$
  select cliente_id from clientes_usuarios where id = auth.uid() and activo
$$;

-- ============ NÚCLEO ============

create type estado_pedido as enum (
  'borrador','confirmado','en_ruta','entregado',
  'fallido','reprogramado','devuelto','cancelado'
);

create table pedidos (
  id                    bigserial primary key,
  cliente_id            int not null references clientes(id),
  referencia_cliente    text,
  -- 'delivery' (D14, Anexo I, changelog acta 4.5): servicio punto a punto ad-hoc, sin retiro
  -- programado — se cotiza y congela en el alta misma, con origen arbitrario (no el depósito).
  tipo                  text not null default 'entrega'
                        check (tipo in ('entrega','retorno','reintento','delivery')),
  pedido_origen_id      bigint references pedidos(id),

  origen_ubicacion_id   bigint not null references ubicaciones(id),
  destino_ubicacion_id  bigint not null references ubicaciones(id),
  destinatario_nombre   text not null,
  destinatario_telefono text not null,              -- RF-07
  bultos                int not null default 1 check (bultos > 0),
  peso_kg               numeric(8,2),
  valor_declarado       numeric(12,2),
  fecha_entrega         date not null,
  urgente               boolean not null default false,

  -- snapshot de precio (P1 / RF-02). Nunca se recalcula. Nullable desde acta changelog 3.11:
  -- el precio depende del tipo de vehículo, que recién se conoce cuando la ruta cierra su
  -- planificación — hasta entonces el pedido está en Borrador, sin ninguno de estos campos.
  -- `peajes` es la excepción: se conoce en el alta, no depende del vehículo.
  zona_id               int references zonas(id),
  precio_base           numeric(12,2),
  -- Anexo I §10.2-N (lectura ii, "precio proporcional al kilometraje recorrido"), changelog acta
  -- 4.5 — recargo_km es un término aditivo sobre precio_base, nulo/cero cuando no hay coordenadas
  -- utilizables. km_cobrados/km_fuente son el snapshot con el que se cotizó (ver DistanciaService
  -- en construccion_v1.md §4.4): 'ruta' (calles, OSRM), 'recta' (haversine) o 'manual'.
  km_cobrados           numeric(6,2) check (km_cobrados is null or km_cobrados >= 0),
  recargo_km            numeric(12,2) not null default 0,
  km_fuente             text check (km_fuente is null or km_fuente in ('ruta','recta','manual')),
  recargo_urgencia      numeric(12,2) default 0,
  descuento_ruta        numeric(12,2) default 0,
  peajes                numeric(12,2) not null default 0,
  total                 numeric(12,2),
  precio_congelado_en   timestamptz,

  -- B9 (Anexo I §4, "+40 km → Cotización"): precio fijado a mano cuando la zona no tiene tarifa
  -- cargada en ningún tipo de vehículo. Sustituye solo el origen de precio_base — la fórmula de
  -- §6 no cambia. Solo editable en Borrador; fn_congelar_pedido lo protege igual que el resto
  -- del precio una vez confirmado (P1). Criterio subjetivo que afecta precio: rastro escrito
  -- (quién, cuándo), igual que el resto de acciones que dejan un `_por`/`_en`.
  precio_manual         numeric(12,2) check (precio_manual is null or precio_manual > 0),
  precio_manual_por     uuid references usuarios(id),
  precio_manual_en      timestamptz,

  -- Override de km cargado a mano cuando ningún proveedor de distancia sirve — mismo criterio
  -- que precio_manual: rastro de quién y cuándo, gana siempre sobre la cascada de DistanciaService.
  km_manual             numeric(6,2) check (km_manual is null or km_manual >= 0),
  km_manual_por         uuid references usuarios(id),
  km_manual_en          timestamptz,

  estado                estado_pedido not null default 'borrador',
  origen_carga          text not null default 'interno'
                        check (origen_carga in ('interno','importado','portal','api')),
  observaciones         text,
  creado_en             timestamptz not null default now(),

  constraint origen_coherente
    check (tipo in ('entrega','delivery') or pedido_origen_id is not null)
);
create index on pedidos (fecha_entrega, estado);
create index on pedidos (cliente_id, fecha_entrega);
create index on pedidos (pedido_origen_id);
-- `tipo` + `pedido_origen_id`: el retorno y el segundo reintento son pedidos
-- nuevos encadenados al original (sección 7). Es la única forma de cobrarlos
-- sin romper P1, y la cadena no se puede reconstruir después.

create table pedido_eventos (
  id                bigserial primary key,
  pedido_id         bigint not null references pedidos(id) on delete cascade,
  estado_anterior   estado_pedido,
  estado_nuevo      estado_pedido not null,
  motivo            text,
  actor_tipo        text not null check (actor_tipo in ('sistema','usuario')),
  actor_usuario_id  uuid references usuarios(id),
  actor_texto       text,                            -- solo actor_tipo='sistema'
  ocurrido_en       timestamptz not null default now(),
  constraint actor_identificado
    check (actor_tipo = 'sistema' or actor_usuario_id is not null)
);
create index on pedido_eventos (pedido_id, ocurrido_en);

-- ============ OPERACIÓN ============

create table vehiculos (
  id                 bigserial primary key,
  patente            text not null unique,     -- normalizada a mayúsculas sin espacios
  descripcion        text,                     -- alias operativo: "Utilitario 1"
  tipo               text not null default 'camioneta'
                     check (tipo in ('camioneta','moto')),  -- acta changelog 3.11
  marca              text,
  modelo             text,
  anio               int check (anio is null or anio between 1950 and 2100),
  km_actual          int,
  vence_vtv          date,
  vence_seguro       date,
  costo_km           numeric(12,2),
  capacidad_paradas  int not null default 24 check (capacidad_paradas > 0),  -- P7 / RF-16
  activo             boolean not null default true,
  creado_en          timestamptz not null default now()
);

create table rutas (
  id                 bigserial primary key,
  fecha              date not null,
  repartidor_id      uuid references usuarios(id),
  vehiculo_id        bigint references vehiculos(id),
  origen_ubicacion_id bigint references ubicaciones(id),  -- null = sin elegir todavía; CerrarPlanificacion lo exige antes de en_curso (acta 3.8)
  capacidad_paradas  int not null default 24,  -- P7 / RF-16
  estado             text not null default 'planificada'
                     check (estado in ('planificada','en_curso','cerrada')),
  km_inicial         int,
  km_final           int,
  combustible_monto  numeric(12,2),
  peajes_monto       numeric(12,2),
  otros_costos       numeric(12,2),
  pago_repartidor    numeric(12,2),
  notas_cierre       text,
  cerrada_en         timestamptz,
  creada_en          timestamptz not null default now()
);
create index on rutas (fecha);

create table ruta_paradas (
  id            bigserial primary key,
  ruta_id       bigint not null references rutas(id) on delete cascade,
  ubicacion_id  bigint not null references ubicaciones(id),
  tipo          text not null check (tipo in ('retiro','entrega','deposito')),
  orden         int not null,
  anclada       boolean not null default false,   -- urgentes: no se reordenan
  estado        text not null default 'pendiente'
                check (estado in ('pendiente','completada','fallida')),
  llegada_en    timestamptz,
  salida_en     timestamptz,                      -- RF-24
  constraint parada_orden_uk unique (ruta_id, orden) deferrable initially deferred
);
-- El unique es DEFERRABLE a propósito: sin eso, cualquier reordenamiento
-- por arrastrar y soltar (RF-12) choca a mitad del UPDATE.
-- `deposito` no se usa hoy. Previsión de 10.2, costo cero.

create table parada_pedidos (
  parada_id  bigint not null references ruta_paradas(id) on delete cascade,
  pedido_id  bigint not null references pedidos(id) on delete cascade,
  primary key (parada_id, pedido_id)
);
-- RF-14: N retiros en la misma dirección = una sola parada.

create table pruebas_entrega (
  id               bigserial primary key,
  pedido_id        bigint not null references pedidos(id) on delete cascade,
  parada_id        bigint references ruta_paradas(id),
  resultado        text not null check (resultado in ('entregado','fallido')),
  motivo_fallo     text,                          -- RF-21, lista cerrada en la app
  receptor_nombre  text,
  identidad_verificada boolean not null default false,   -- RF-23, sin imagen
  foto_path        text,
  lat              numeric(10,7),
  lng              numeric(10,7),
  desvio_metros    int,                           -- RF-29
  capturada_en     timestamptz not null,          -- RNF-03: hora de la calle
  sincronizada_en  timestamptz not null default now(),
  device_uuid      text not null                  -- RNF-02: idempotencia
);
create unique index on pruebas_entrega (pedido_id, device_uuid);
create index on pruebas_entrega (capturada_en);

-- ============ REGISTRO SIN MAQUINARIA (P4 / RF-31) ============

create table tipos_evento_cliente (
  id         serial primary key,
  codigo     text not null unique,
  dimension  text not null check (dimension in ('pago','trato','operacion')),
  descripcion text not null
);

create table eventos_cliente (
  id            bigserial primary key,
  cliente_id    int not null references clientes(id),
  tipo_id       int not null references tipos_evento_cliente(id),
  pedido_id     bigint references pedidos(id),
  valor_num     numeric(12,2),                   -- días de atraso, monto, etc.
  nota          text,
  ocurrido_en   timestamptz not null default now(),
  registrado_por uuid references usuarios(id)
);
create index on eventos_cliente (cliente_id, ocurrido_en);
-- Se acumulan desde el día uno. Nadie los procesa hasta los 6 meses.

-- =====================================================================
-- TRIGGERS — las reglas viven acá, no en la aplicación (RNF-04)
-- =====================================================================

-- 1. Log automático de toda transición de estado (P2 / RF-28)
create or replace function fn_log_estado_pedido()
returns trigger language plpgsql security definer set search_path = public as $$
declare v_actor uuid := auth.uid();
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
end $$;

create trigger trg_log_estado_pedido
  after insert or update on pedidos
  for each row execute function fn_log_estado_pedido();

-- 2. El log es inmutable (RF-28)
create or replace function fn_log_inmutable()
returns trigger language plpgsql as $$
begin
  raise exception 'pedido_eventos es de solo inserción';
end $$;

create trigger trg_log_inmutable
  before update or delete on pedido_eventos
  for each row execute function fn_log_inmutable();

-- 3. El precio y el destino se congelan al confirmar (P1 / RF-02 / criterio 7)
-- precio_manual entra a este conjunto desde Anexo I B9: sin esto sería el único campo de
-- precio editable después de confirmar. km_cobrados/recargo_km/km_fuente/km_manual entran desde
-- Anexo I §10.2-N (changelog acta 4.5), mismo criterio.
create or replace function fn_congelar_pedido()
returns trigger language plpgsql as $$
begin
  if old.estado <> 'borrador' and (
       new.precio_base      is distinct from old.precio_base
    or new.recargo_urgencia is distinct from old.recargo_urgencia
    or new.descuento_ruta   is distinct from old.descuento_ruta
    or new.peajes           is distinct from old.peajes
    or new.total            is distinct from old.total
    or new.precio_manual    is distinct from old.precio_manual
    or new.km_cobrados      is distinct from old.km_cobrados
    or new.recargo_km       is distinct from old.recargo_km
    or new.km_fuente        is distinct from old.km_fuente
    or new.km_manual        is distinct from old.km_manual
    or new.destino_ubicacion_id is distinct from old.destino_ubicacion_id
  ) then
    raise exception
      'Pedido % confirmado: precio y destino no se modifican (P1)', old.id;
  end if;
  return new;
end $$;

create trigger trg_congelar_pedido
  before update on pedidos
  for each row execute function fn_congelar_pedido();

-- 4. Una dirección dudosa no entra a una ruta (RF-05 / criterio 6)
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
end $$;

create trigger trg_bloquear_direccion_dudosa
  before insert on parada_pedidos
  for each row execute function fn_bloquear_direccion_dudosa();

-- 5. Un mismo email no puede estar en usuarios y clientes_usuarios a la vez (si no, el login
--    por email queda ambiguo). Un índice único no alcanza porque son dos tablas distintas.
create or replace function fn_verificar_email_unico_usuarios()
returns trigger language plpgsql as $$
begin
  if exists (select 1 from clientes_usuarios where email = new.email) then
    raise exception 'El email % ya está en uso por un login de cliente.', new.email;
  end if;
  return new;
end $$;

create trigger trg_verificar_email_unico_usuarios
  before insert or update of email on usuarios
  for each row execute function fn_verificar_email_unico_usuarios();

create or replace function fn_verificar_email_unico_clientes_usuarios()
returns trigger language plpgsql as $$
begin
  if exists (select 1 from usuarios where email = new.email) then
    raise exception 'El email % ya está en uso por un usuario interno.', new.email;
  end if;
  return new;
end $$;

create trigger trg_verificar_email_unico_clientes_usuarios
  before insert or update of email on clientes_usuarios
  for each row execute function fn_verificar_email_unico_clientes_usuarios();

-- =====================================================================
-- RLS — sin esto, la anon key de Supabase lee todo (RNF-08)
-- =====================================================================

alter table zonas                enable row level security;
alter table localidades          enable row level security;
alter table ubicaciones          enable row level security;
alter table clientes             enable row level security;
alter table tarifas              enable row level security;
alter table usuarios             enable row level security;
alter table pedidos              enable row level security;
alter table pedido_eventos       enable row level security;
alter table rutas                enable row level security;
alter table ruta_paradas         enable row level security;
alter table parada_pedidos       enable row level security;
alter table pruebas_entrega      enable row level security;
alter table tipos_evento_cliente enable row level security;
alter table eventos_cliente      enable row level security;

-- Administración y operación: acceso completo a todo.
do $$
declare t text;
begin
  foreach t in array array[
    'zonas','localidades','ubicaciones','clientes','tarifas','usuarios',
    'pedidos','pedido_eventos','rutas','ruta_paradas','parada_pedidos',
    'pruebas_entrega','tipos_evento_cliente','eventos_cliente'
  ] loop
    execute format(
      'create policy back_office on %I for all to authenticated
         using (mi_rol() in (''administracion'',''operacion''))
         with check (mi_rol() in (''administracion'',''operacion''))', t);
  end loop;
end $$;

-- Repartidor: su ruta del día y nada más. No ve precios (ver vista abajo).
create policy repartidor_rutas on rutas for select to authenticated
  using (mi_rol() = 'repartidor' and repartidor_id = auth.uid());

create policy repartidor_paradas on ruta_paradas for select to authenticated
  using (mi_rol() = 'repartidor' and exists (
    select 1 from rutas r where r.id = ruta_id and r.repartidor_id = auth.uid()));

create policy repartidor_avanza on ruta_paradas for update to authenticated
  using (mi_rol() = 'repartidor' and exists (
    select 1 from rutas r where r.id = ruta_id and r.repartidor_id = auth.uid()));

create policy repartidor_prueba on pruebas_entrega for insert to authenticated
  with check (mi_rol() = 'repartidor');

create policy repartidor_ubicaciones on ubicaciones for select to authenticated
  using (mi_rol() = 'repartidor');

-- Cliente: solo sus pedidos, sin colores ni costos internos. mi_cliente() ya resuelve null si
-- quien está logueado no es un login de clientes_usuarios, así que alcanza con esa condición.
create policy cliente_pedidos on pedidos for select to authenticated
  using (cliente_id = mi_cliente());

-- Vista para la PWA: todo lo que el repartidor necesita, ningún importe.
create or replace view v_paradas_repartidor
with (security_invoker = true) as
select rp.id as parada_id, rp.ruta_id, rp.orden, rp.tipo, rp.estado,
       rp.llegada_en, rp.salida_en,
       p.id as pedido_id, p.destinatario_nombre, p.destinatario_telefono,
       p.bultos, p.observaciones,
       u.calle_numero, u.referencia, u.lat, u.lng, l.nombre as localidad
from ruta_paradas rp
join parada_pedidos pp on pp.parada_id = rp.id
join pedidos p         on p.id = pp.pedido_id
join ubicaciones u     on u.id = rp.ubicacion_id
left join localidades l on l.id = u.localidad_id;

-- =====================================================================
-- SEED MÍNIMO
-- =====================================================================

insert into zonas (codigo, nombre) values
  ('A','Cercana'), ('B','Media'), ('C','Lejana'), ('D','Muy lejana');

insert into tipos_evento_cliente (codigo, dimension, descripcion) values
  ('pago_termino',      'pago',      'Pago dentro del plazo'),
  ('pago_tardio',       'pago',      'Pago fuera de plazo (valor_num = días)'),
  ('impago',            'pago',      'Comprobante vencido sin pago'),
  ('rechazo_injust',    'trato',     'Rechazo injustificado del pedido'),
  ('cambio_ruta_armada','trato',     'Cambio con la ruta ya planificada'),
  ('reclamo_desest',    'trato',     'Reclamo desestimado'),
  ('direccion_erronea', 'operacion', 'Dirección incorrecta provista por el cliente'),
  ('destinatario_ausente','operacion','Destinatario ausente'),
  ('entrega_ok',        'operacion', 'Entrega sin incidente'),
  ('reserva_anticipada','operacion', 'Pedido cargado antes del corte');

-- Faltan: localidades del área de cobertura y las filas de `tarifas`
-- (una por zona con cliente_id null, más las de cada cliente si difieren).
-- Sin tarifas cargadas no se puede dar de alta un pedido.
