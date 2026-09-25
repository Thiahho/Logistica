namespace Logistica.Entidades;

public class Pedido
{
    public long Id { get; set; }

    public int ClienteId { get; set; }
    public Cliente Cliente { get; set; } = null!;

    public string? ReferenciaCliente { get; set; }

    /// <summary>entrega | retorno | reintento</summary>
    public string Tipo { get; set; } = "entrega";
    public long? PedidoOrigenId { get; set; }
    public Pedido? PedidoOrigen { get; set; }

    public long OrigenUbicacionId { get; set; }
    public Ubicacion OrigenUbicacion { get; set; } = null!;

    public long DestinoUbicacionId { get; set; }
    public Ubicacion DestinoUbicacion { get; set; } = null!;

    public string DestinatarioNombre { get; set; } = null!;
    public string DestinatarioTelefono { get; set; } = null!;
    public int Bultos { get; set; } = 1;
    public decimal? PesoKg { get; set; }
    public decimal? ValorDeclarado { get; set; }
    public DateOnly FechaEntrega { get; set; }
    public bool Urgente { get; set; }

    // Snapshot de precio (P1 / RF-02). Nunca se recalcula tras confirmar; trg_congelar_pedido lo impide.
    // Nullable desde acta changelog 3.11: el precio depende del tipo de vehículo (camioneta/moto),
    // que recién se conoce cuando la ruta que lo lleva cierra su planificación — hasta entonces el
    // pedido queda en Borrador, sin ninguno de estos campos. `Peajes` es la excepción: es un dato
    // que el cliente ya conoce al cargar el pedido, no depende del vehículo, se guarda de entrada.
    public int? ZonaId { get; set; }
    public Zona? Zona { get; set; }
    public decimal? PrecioBase { get; set; }
    public decimal? RecargoUrgencia { get; set; }
    public decimal? DescuentoRuta { get; set; }

    /// <summary>B3, definición J (acta changelog 4.21): descuento del rango efectivo del cliente al
    /// cotizar, solo sobre la tarifa general. Congelado con el resto del precio (fn_congelar_pedido).</summary>
    public decimal DescuentoRango { get; set; }
    public decimal Peajes { get; set; }
    public decimal? Total { get; set; }
    public DateTimeOffset? PrecioCongeladoEn { get; set; }

    // B9 (Anexo I §4): zona sin tarifa cargada ("+40 km → Cotización"). Precio fijado a mano por
    // administración en vez de tarifa_vigente — sustituye solo el origen de precio_base, la
    // fórmula de §6 (recargo, descuento, peajes) sigue igual encima. Solo editable en Borrador;
    // fn_congelar_pedido protege esta columna igual que el resto del precio (P1).
    // Desde B5 (diseño_b5_portal_carga.md §1) también puede ser el id de un clientes_usuarios (el
    // cliente cotizó su propio precio vinculante desde el portal) — por eso sin FK ni navegación:
    // no hay una sola tabla a la que apuntar. Se resuelve a mano probando usuarios primero y
    // clientes_usuarios después (PedidosController.Detalle).
    public decimal? PrecioManual { get; set; }
    public Guid? PrecioManualPor { get; set; }
    public DateTimeOffset? PrecioManualEn { get; set; }

    // Anexo I §10.2-N (lectura ii, "precio proporcional al kilometraje recorrido") — recargo
    // aditivo sobre precio_base, nulo/cero cuando no hay coordenadas utilizables (Servicios/
    // DistanciaService.cs). KmCobrados/KmFuente son el snapshot con el que se cotizó, protegido
    // por fn_congelar_pedido igual que el resto del precio (P1).
    public decimal? KmCobrados { get; set; }
    public decimal RecargoKm { get; set; }
    /// <summary>ruta | recta | manual — trazabilidad de con qué número se cobró.</summary>
    public string? KmFuente { get; set; }

    // Override cargado a mano cuando ningún proveedor de distancia sirve — mismo criterio que
    // PrecioManual: rastro de quién y cuándo porque es un criterio subjetivo que afecta precio.
    public decimal? KmManual { get; set; }
    public Guid? KmManualPor { get; set; }
    public Usuario? KmManualPorUsuario { get; set; }
    public DateTimeOffset? KmManualEn { get; set; }

    public EstadoPedido Estado { get; set; } = EstadoPedido.Borrador;

    /// <summary>interno | importado | portal | api</summary>
    public string OrigenCarga { get; set; } = "interno";
    public string? Observaciones { get; set; }
    public DateTimeOffset CreadoEn { get; set; }

    /// <summary>Login del portal que cargó el pedido (null si lo cargó el personal interno). Columna
    /// propia y no precio_manual_por: ese queda null cuando la zona no tiene tarifa (B9) y
    /// RutasController lo usa para reconocer el precio vinculante del portal.</summary>
    public Guid? CreadoPorClienteUsuarioId { get; set; }
    public ClienteUsuario? CreadoPorClienteUsuario { get; set; }

    /// <summary>Viaje (envío de varias paradas) al que pertenece, y su lugar en el orden con que se
    /// cargó el viaje. null para un envío suelto.</summary>
    public long? ViajeId { get; set; }
    public Viaje? Viaje { get; set; }
    public int? OrdenEnViaje { get; set; }

    // B13 (diseño_b13_recepcion_portal.md §3): snapshot de lo que el cliente cargó en el portal.
    // Se completa una sola vez al alta, solo si OrigenCarga = "portal"; nunca se vuelve a escribir
    // desde ningún endpoint, así que no necesita un trigger de inmutabilidad como
    // retiro_bultos_contados (ese lo firma un dispositivo fuera del control del backend, esto no).
    public int? BultosDeclaradoCliente { get; set; }
    public DateTimeOffset? RecepcionConfirmadaEn { get; set; }
    public Guid? RecepcionConfirmadaPor { get; set; }
    public Usuario? RecepcionConfirmadaPorUsuario { get; set; }
}
