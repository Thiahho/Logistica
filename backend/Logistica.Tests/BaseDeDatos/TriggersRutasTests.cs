using Logistica.Entidades;

namespace Logistica.Tests.BaseDeDatos;

// Acta §7 (lo que declaró la calle no se toca) y RF-41 (una ruta liquidada no cambia su pago):
// trg_congelar_declaracion_repartidor, trg_rutas_liquidada, trg_liquidaciones_inmutable y
// trg_novedades_inmutable.
[Collection(ColeccionBaseDeDatos.Nombre)]
[Trait("Categoria", "BaseDeDatos")]
public class TriggersRutasTests(BaseDePrueba baseDatos)
{
    private const string Uuid = "0b6f8f3e-4a43-4c0e-9d51-6a1d2f3b4c5d";

    private async Task<Ruta> RutaConRetiroFirmadoAsync()
    {
        var ruta = await baseDatos.RutaAsync();
        await baseDatos.EjecutarAsync($"""
            update rutas set retiro_confirmado_en = now(), retiro_bultos_esperados = 10, retiro_bultos_contados = 10,
                             retiro_firma_path = 'retiros/1/firma.jpg', retiro_km_inicial = 50000, retiro_device_uuid = '{Uuid}'
            where id = {ruta.Id}
            """);
        return ruta;
    }

    private async Task<Ruta> RutaConCierreDeclaradoAsync()
    {
        var ruta = await RutaConRetiroFirmadoAsync();
        await baseDatos.EjecutarAsync($"""
            update rutas set cierre_repartidor_en = now(), cierre_repartidor_km_final = 50120, cierre_repartidor_combustible = 9000,
                             cierre_repartidor_peajes = 1200, cierre_repartidor_device_uuid = '{Uuid}'
            where id = {ruta.Id}
            """);
        return ruta;
    }

    private async Task<(Ruta Ruta, Liquidacion Liquidacion)> RutaLiquidadaAsync()
    {
        var administrador = await baseDatos.UsuarioAsync();
        var repartidor = await baseDatos.UsuarioAsync(Roles.Repartidor);
        var ruta = await baseDatos.RutaAsync(repartidor.Id);
        var liquidacion = await baseDatos.LiquidacionAsync(repartidor.Id, administrador.Id);
        await baseDatos.EjecutarAsync(
            $"update rutas set estado = 'cerrada', pago_repartidor = 5000, liquidacion_id = {liquidacion.Id} where id = {ruta.Id}");
        return (ruta, liquidacion);
    }

    private async Task<long> NovedadAsync()
    {
        var repartidor = await baseDatos.UsuarioAsync(Roles.Repartidor);
        var ruta = await baseDatos.RutaAsync(repartidor.Id);
        return await baseDatos.EscalarAsync<long>($"""
            insert into novedades (ruta_id, tipo, origen, categoria, descripcion, creada_por, creada_en)
            values ({ruta.Id}, 'incidencia_ruta', 'repartidor', 'averia', 'Pinchadura en ruta 3', '{repartidor.Id}', now())
            returning id
            """);
    }

    // ---- trg_congelar_declaracion_repartidor ----

    [Theory]
    [InlineData("retiro_confirmado_en = now() + interval '1 hour'")]
    [InlineData("retiro_bultos_esperados = 11, retiro_bultos_contados = 11")]
    [InlineData("retiro_bultos_contados = 9, retiro_observaciones = 'faltó uno'")]
    [InlineData("retiro_observaciones = 'agregado después'")]
    [InlineData("retiro_firma_path = 'retiros/1/otra.jpg'")]
    [InlineData("retiro_km_inicial = 1")]
    [InlineData("retiro_device_uuid = null")]
    public async Task El_retiro_firmado_no_se_edita(string asignacion)
    {
        var ruta = await RutaConRetiroFirmadoAsync();

        await baseDatos.RechazaPorTriggerAsync(
            $"update rutas set {asignacion} where id = {ruta.Id}", "el retiro firmado por el repartidor no se edita");
    }

    [Theory]
    [InlineData("cierre_repartidor_en = now() + interval '1 hour'")]
    [InlineData("cierre_repartidor_km_final = 50000")]
    [InlineData("cierre_repartidor_combustible = 1")]
    [InlineData("cierre_repartidor_peajes = 1")]
    [InlineData("cierre_repartidor_notas = 'agregado después'")]
    [InlineData("cierre_repartidor_device_uuid = null")]
    public async Task El_cierre_de_jornada_del_repartidor_no_se_edita(string asignacion)
    {
        var ruta = await RutaConCierreDeclaradoAsync();

        await baseDatos.RechazaPorTriggerAsync(
            $"update rutas set {asignacion} where id = {ruta.Id}", "el cierre de jornada del repartidor no se edita");
    }

    [Fact]
    public async Task Administracion_cierra_con_sus_propias_columnas_sin_tocar_la_declaracion()
    {
        var ruta = await RutaConCierreDeclaradoAsync();

        var filas = await baseDatos.EjecutarAsync($"""
            update rutas set estado = 'cerrada', km_inicial = 50000, km_final = 50118, combustible_monto = 8800,
                             notas_cierre = 'El ticket de combustible dice 8.800', cerrada_en = now()
            where id = {ruta.Id}
            """);

        Assert.Equal(1, filas);
    }

    [Fact]
    public async Task Un_retiro_con_diferencia_de_bultos_exige_observacion()
    {
        var ruta = await baseDatos.RutaAsync();

        var error = await Assert.ThrowsAsync<Npgsql.PostgresException>(() => baseDatos.EjecutarAsync($"""
            update rutas set retiro_confirmado_en = now(), retiro_bultos_esperados = 10, retiro_bultos_contados = 9
            where id = {ruta.Id}
            """));

        Assert.Equal("ck_rutas_retiro_discrepancia_observada", error.ConstraintName);
    }

    // ---- trg_rutas_liquidada ----

    [Theory]
    [InlineData("pago_repartidor = 9000")]
    [InlineData("liq_entregas = 20")]
    [InlineData("liq_fallidas_imputables = 1")]
    [InlineData("liq_pct_exito = 100")]
    [InlineData("liq_pago_entregas = 9000")]
    [InlineData("liq_bono = 500")]
    [InlineData("pago_ajuste_motivo = 'ajuste tardío'")]
    [InlineData("liquidacion_id = null")]
    public async Task El_pago_de_una_ruta_liquidada_no_se_modifica(string asignacion)
    {
        var (ruta, _) = await RutaLiquidadaAsync();

        await baseDatos.RechazaPorTriggerAsync(
            $"update rutas set {asignacion} where id = {ruta.Id}", "su pago no se modifica");
    }

    [Fact]
    public async Task Una_ruta_liquidada_no_se_borra()
    {
        var (ruta, _) = await RutaLiquidadaAsync();

        await baseDatos.RechazaPorTriggerAsync($"delete from rutas where id = {ruta.Id}", "no se puede borrar");
    }

    [Fact]
    public async Task Una_ruta_liquidada_admite_cambios_que_no_son_del_pago()
    {
        var (ruta, _) = await RutaLiquidadaAsync();

        var filas = await baseDatos.EjecutarAsync($"update rutas set notas_cierre = 'Aclaración' where id = {ruta.Id}");

        Assert.Equal(1, filas);
    }

    [Fact]
    public async Task Una_ruta_sin_liquidar_admite_corregir_el_pago()
    {
        var ruta = await baseDatos.RutaAsync();
        await baseDatos.EjecutarAsync($"update rutas set estado = 'cerrada', pago_repartidor = 5000 where id = {ruta.Id}");

        var filas = await baseDatos.EjecutarAsync($"update rutas set pago_repartidor = 5500 where id = {ruta.Id}");

        Assert.Equal(1, filas);
    }

    // ---- trg_liquidaciones_inmutable ----

    [Theory]
    [InlineData("update liquidaciones set total = 1 where id = {0}")]
    [InlineData("update liquidaciones set nota = 'corregida' where id = {0}")]
    [InlineData("delete from liquidaciones where id = {0}")]
    public async Task Una_liquidacion_emitida_no_se_edita_ni_se_borra(string sql)
    {
        var (_, liquidacion) = await RutaLiquidadaAsync();

        await baseDatos.RechazaPorTriggerAsync(
            string.Format(sql, liquidacion.Id), "una liquidación emitida no se edita ni se borra");
    }

    // ---- trg_novedades_inmutable ----

    [Theory]
    [InlineData("descripcion = 'Otra cosa'")]
    [InlineData("categoria = 'accidente'")]
    [InlineData("foto_path = 'novedades/1/otra.jpg'")]
    [InlineData("creada_en = now() - interval '1 day'")]
    [InlineData("device_uuid = 'otro'")]
    public async Task Lo_que_informo_quien_creo_la_novedad_no_se_edita(string asignacion)
    {
        var novedad = await NovedadAsync();

        await baseDatos.RechazaPorTriggerAsync(
            $"update novedades set {asignacion} where id = {novedad}", "lo que informó quien la creó no se edita");
    }

    [Theory]
    [InlineData("resuelta")]
    [InlineData("rechazada")]
    public async Task Una_novedad_abierta_se_resuelve_o_se_rechaza(string estado)
    {
        var novedad = await NovedadAsync();

        var filas = await baseDatos.EjecutarAsync(
            $"update novedades set estado = '{estado}', resolucion = 'Atendida', resuelta_en = now() where id = {novedad}");

        Assert.Equal(1, filas);
    }

    [Theory]
    [InlineData("resuelta", "abierta")]
    [InlineData("resuelta", "rechazada")]
    [InlineData("rechazada", "abierta")]
    [InlineData("rechazada", "resuelta")]
    public async Task Una_novedad_cerrada_no_cambia_de_estado(string cerrada, string despues)
    {
        var novedad = await NovedadAsync();
        await baseDatos.EjecutarAsync($"update novedades set estado = '{cerrada}' where id = {novedad}");

        await baseDatos.RechazaPorTriggerAsync(
            $"update novedades set estado = '{despues}' where id = {novedad}", "no vuelve a abrirse");
    }

    [Fact]
    public async Task Una_novedad_cerrada_se_puede_marcar_como_vista()
    {
        var novedad = await NovedadAsync();
        await baseDatos.EjecutarAsync($"update novedades set estado = 'resuelta' where id = {novedad}");

        var filas = await baseDatos.EjecutarAsync($"update novedades set visto_en = now() where id = {novedad}");

        Assert.Equal(1, filas);
    }
}
