namespace Logistica.Opciones;

/// <summary>
/// RF-08: hora de corte de la carga del día siguiente (Dominio/CorteDeCarga.cs), para el alta interna
/// y la del portal. 18:00 es la del acta §7. No confundir con Portal:HoraCorte, que corta la carga del
/// portal para el mismo día.
/// </summary>
public class OpcionesCarga
{
    public TimeOnly HoraCorteDiaSiguiente { get; set; } = new(18, 0);
}
