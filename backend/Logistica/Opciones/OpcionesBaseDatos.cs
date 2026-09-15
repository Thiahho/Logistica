namespace Logistica.Opciones;

/// <summary>
/// Tuning del pool de conexiones Npgsql y de la resiliencia de EF Core, aplicado por
/// Datos/RegistroDatos.cs sobre la cadena de conexión y el DbContextOptionsBuilder — la cadena
/// de conexión en sí (appsettings.json / user-secrets) sigue siendo solo credenciales.
///
/// Los valores de arranque en appsettings.json asumen un Postgres GESTIONADO (Supabase/Neon/RDS,
/// no el docker-compose.yml del repo ni el Postgres nativo local): ahí el techo de conexiones lo
/// impone el plan del proveedor, no el proceso, y cada round-trip cuesta latencia de red real
/// (5-20ms) en vez de ser prácticamente gratis como contra el Postgres local.
/// </summary>
public class OpcionesBaseDatos
{
    /// <summary>Techo de conexiones del pool de ESTE proceso. 100 (default de Npgsql) asume que
    /// el proceso es dueño de todo el límite de conexiones de Postgres — falso contra un
    /// gestionado, donde el plan típicamente permite 15-60 conexiones en total y hay que dejar
    /// margen para otras instancias/servicios.</summary>
    public int MaxPoolSize { get; set; } = 20;

    /// <summary>Conexiones que el pool mantiene abiertas aunque no haya tráfico. 0 (default)
    /// significa que el primer request después de un rato ocioso paga el handshake TLS + auth
    /// completo contra un host remoto.</summary>
    public int MinPoolSize { get; set; } = 2;

    /// <summary>Segundos que una conexión puede estar ociosa en el pool antes de reciclarse. Los
    /// gestionados suelen cortar conexiones ociosas del lado servidor; reciclarla antes evita
    /// que el pool entregue una conexión ya muerta.</summary>
    public int ConnectionIdleLifetimeSegundos { get; set; } = 60;

    /// <summary>Segundos de espera al abrir una conexión nueva antes de fallar. 10, no los 15 de
    /// default: mejor fallar rápido y claro que colgar el request.</summary>
    public int TimeoutSegundos { get; set; } = 10;

    /// <summary>Segundos de espera de un comando SQL antes de fallar. Explícito acá en vez de
    /// heredar el default de Npgsql (30s), para que quede documentado por qué es ese valor.</summary>
    public int CommandTimeoutSegundos { get; set; } = 30;

    /// <summary>Segundos entre keepalive TCP. Sin esto, un NAT/firewall intermedio puede cortar
    /// una conexión ociosa sin que ni el cliente ni Postgres se enteren hasta el próximo uso.</summary>
    public int KeepaliveSegundos { get; set; } = 30;

    /// <summary>true cuando ConnectionStrings:Postgres apunta a un pooler externo en modo
    /// transacción (Supabase puerto 6543, Neon con sufijo "-pooler") en vez de al Postgres
    /// directo. En ese modo el pooler ya resetea la sesión al devolver la conexión y no soporta
    /// prepared statements con nombre — ver Datos/RegistroDatos.cs.
    ///
    /// A verificar antes de poner esto en true contra un proveedor real: LogisticaDbContext
    /// mapea el enum estado_pedido al abrir la conexión física (NpgsqlDataSourceBuilder.MapEnum),
    /// lo que necesita leer los OIDs del tipo desde el catálogo de Postgres — normalmente
    /// funciona detrás de un pooler en modo transacción, pero no está probado contra ninguno en
    /// particular. Si falla, la salida es usar el endpoint directo para la app.</summary>
    public bool DetrasDePoolerExterno { get; set; }
}
