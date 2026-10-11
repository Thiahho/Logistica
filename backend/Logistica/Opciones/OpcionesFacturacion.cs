namespace Logistica.Opciones;

/// <summary>Cierre automático de ciclos de facturación (Servicios/CierreCiclosAutomatico.cs).</summary>
public class OpcionesFacturacion
{
    /// <summary>null = prendido salvo en Development: una factura emitida no se borra
    /// (trg_facturas_inmutable), y levantar el backend en una máquina de desarrollo no tiene que emitir
    /// facturas sobre los datos de prueba. true/false lo fuerza en cualquier entorno.</summary>
    public bool? CierreAutomatico { get; set; }

    /// <summary>Cada cuánto se revisa si hay ciclos por cerrar. El cierre es idempotente y recupera
    /// solo los días salteados, así que el intervalo no define qué se factura, solo cuán pronto.</summary>
    public int IntervaloHoras { get; set; } = 6;
}
