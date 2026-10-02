using Logistica.Entidades;
using Logistica.Servicios;

namespace Logistica.Tests;

// B6 (Anexo I §5, E4): texto del resumen diario de envíos y cuándo cierra el día del resumen. El envío
// contra la base se prueba en BaseDeDatos/AvisosEstadoBaseTests.
public class ResumenEnviosTests
{
    private static readonly DateOnly Dia = new(2026, 10, 2);

    private static MovimientoEnvio Envio(long id, EstadoPedido estado, string? motivo = null) =>
        new(id, $"Destinatario {id}", $"Calle {id}, San Justo", estado, motivo);

    [Fact]
    public void Sin_movimientos_no_hay_correo() =>
        Assert.Null(ResumenEnvios.Armar("Acme", Dia, []));

    [Fact]
    public void El_resumen_agrupa_por_estado_y_explica_los_fallidos()
    {
        var mensaje = ResumenEnvios.Armar("Acme", Dia,
        [
            Envio(12, EstadoPedido.Entregado),
            Envio(10, EstadoPedido.Entregado),
            Envio(15, EstadoPedido.Fallido, "destinatario_ausente"),
            Envio(18, EstadoPedido.EnRuta),
        ])!;

        Assert.Equal("Tus envíos del 02/10: 2 entregados, 1 sin entregar", mensaje.Asunto);
        Assert.Equal(
            """
            Hola Acme: este es el resumen de tus envíos del 02/10.

            Entregados (2):
            - #10 · Destinatario 10 · Calle 10, San Justo
            - #12 · Destinatario 12 · Calle 12, San Justo

            No se pudieron entregar (1):
            - #15 · Destinatario 15 · Calle 15, San Justo — destinatario ausente

            Siguen en reparto (1):
            - #18 · Destinatario 18 · Calle 18, San Justo

            — Logística
            """.ReplaceLineEndings("\n"),
            mensaje.Cuerpo);
    }

    [Fact]
    public void Sin_entregados_ni_fallidos_el_asunto_es_generico()
    {
        var mensaje = ResumenEnvios.Armar("Acme", Dia, [Envio(1, EstadoPedido.Cancelado, "Lo pidió el cliente")])!;

        Assert.Equal("Resumen de tus envíos del 02/10", mensaje.Asunto);
        Assert.Contains("Cancelados (1):\n- #1 · Destinatario 1 · Calle 1, San Justo — Lo pidió el cliente", mensaje.Cuerpo);
    }

    [Fact]
    public void Una_seccion_larga_se_recorta_y_dice_cuantos_quedan_afuera()
    {
        var muchos = Enumerable.Range(1, ResumenEnvios.MaxRenglonesPorSeccion + 5).Select(i => Envio(i, EstadoPedido.Entregado)).ToList();

        var mensaje = ResumenEnvios.Armar("Acme", Dia, muchos)!;

        Assert.Contains("Entregados (45):", mensaje.Cuerpo);
        Assert.Contains("… y 5 más.", mensaje.Cuerpo);
        Assert.DoesNotContain("#41 ·", mensaje.Cuerpo);
    }

    [Fact]
    public void Los_pasos_internos_no_son_informables()
    {
        Assert.DoesNotContain(EstadoPedido.Borrador, ResumenEnvios.Informables);
        Assert.DoesNotContain(EstadoPedido.Confirmado, ResumenEnvios.Informables);
    }

    // ---- Cuándo cierra el día del resumen (hora local de Argentina, UTC-3) ----

    private static readonly TimeOnly Hora = new(20, 0);

    [Fact]
    public void Pasada_la_hora_el_ultimo_cierre_es_el_de_hoy()
    {
        var ahora = new DateTimeOffset(2026, 10, 2, 23, 30, 0, TimeSpan.Zero); // 20:30 local

        Assert.Equal(new DateTimeOffset(2026, 10, 2, 23, 0, 0, TimeSpan.Zero), AvisosEstadoService.UltimoCierre(ahora, Hora));
    }

    [Fact]
    public void Antes_de_la_hora_el_ultimo_cierre_es_el_de_ayer()
    {
        var ahora = new DateTimeOffset(2026, 10, 2, 12, 0, 0, TimeSpan.Zero); // 09:00 local

        Assert.Equal(new DateTimeOffset(2026, 10, 1, 23, 0, 0, TimeSpan.Zero), AvisosEstadoService.UltimoCierre(ahora, Hora));
    }

    [Fact]
    public void De_madrugada_en_UTC_todavia_es_el_dia_local_anterior()
    {
        var ahora = new DateTimeOffset(2026, 10, 3, 1, 0, 0, TimeSpan.Zero); // 22:00 local del 02/10

        Assert.Equal(new DateTimeOffset(2026, 10, 2, 23, 0, 0, TimeSpan.Zero), AvisosEstadoService.UltimoCierre(ahora, Hora));
    }
}
