using ClosedXML.Excel;
using Logistica.Dominio;
using Logistica.Servicios;

namespace Logistica.Tests;

// B11 (Anexo I §5, E4): interpretación de una fila de la planilla y lectura del Excel. El alta contra
// la base se prueba en BaseDeDatos/ImportacionPedidosBaseTests.
public class ImportacionPedidosTests
{
    private static readonly DateOnly Hoy = new(2026, 10, 2);
    private static readonly TimeOnly Ahora = new(10, 0);
    private static readonly TimeOnly Corte = new(18, 0);

    private static readonly LocalidadImportable[] Localidades =
    [
        new(1, "San Justo", "La Matanza", true),
        new(2, "San Martín", "General San Martín", true),
        new(3, "San Martín", "Otro Partido", true),
        new(4, "Sin Zona", null, false),
    ];

    private static FilaPlanilla Fila(
        string? destinatario = "Juan Pérez", string? telefono = "11 5555-1234", string? calle = "Av. Mitre 1234",
        string? localidad = "San Justo", string? partido = null, string? bultos = null, string? fecha = "05/10/2026",
        string? peso = null, string? valor = null, string? urgente = null) =>
        new(7, destinatario, telefono, calle, localidad, partido, null, bultos, fecha, null, null, peso, valor, urgente);

    private static FilaValidada Validar(FilaPlanilla fila, TimeOnly? ahora = null) =>
        ImportacionPedidos.Validar(fila, Hoy, ahora ?? Ahora, Corte, Localidades);

    [Fact]
    public void Una_fila_completa_se_interpreta()
    {
        var resultado = Validar(Fila(bultos: "3", peso: "12,5", valor: "150000", urgente: "Sí"));

        Assert.Empty(resultado.Errores);
        var pedido = resultado.Pedido!;
        Assert.Equal(7, resultado.Numero);
        Assert.Equal("Juan Pérez", pedido.DestinatarioNombre);
        Assert.Equal(1, pedido.LocalidadId);
        Assert.Equal(3, pedido.Bultos);
        Assert.Equal(new DateOnly(2026, 10, 5), pedido.FechaEntrega);
        Assert.Equal(12.5m, pedido.PesoKg);
        Assert.Equal(150000m, pedido.ValorDeclarado);
        Assert.True(pedido.Urgente);
    }

    [Fact]
    public void Sin_bultos_ni_urgente_valen_los_del_alta_de_a_uno()
    {
        var pedido = Validar(Fila()).Pedido!;

        Assert.Equal(1, pedido.Bultos);
        Assert.False(pedido.Urgente);
    }

    [Theory]
    [InlineData("2026-10-05")]   // celda con formato de fecha
    [InlineData("05/10/2026")]
    [InlineData("5/10/2026")]
    [InlineData("5-10-2026")]
    public void La_fecha_se_lee_como_dia_mes_anio(string fecha) =>
        Assert.Equal(new DateOnly(2026, 10, 5), Validar(Fila(fecha: fecha)).Pedido!.FechaEntrega);

    [Fact]
    public void Una_fila_vacia_de_datos_obligatorios_lista_todo_lo_que_falta()
    {
        var resultado = Validar(Fila(destinatario: " ", telefono: null, calle: "", localidad: null, fecha: null));

        Assert.Null(resultado.Pedido);
        Assert.Equal(
            ["Falta el destinatario.", "Falta el teléfono.", "Falta la calle y número.", "Falta la localidad.", "Falta la fecha de entrega."],
            resultado.Errores);
    }

    [Theory]
    [InlineData("123")]
    [InlineData("no tiene")]
    public void Un_telefono_invalido_se_rechaza(string telefono) =>
        Assert.Contains("no es válido", Assert.Single(Validar(Fila(telefono: telefono)).Errores));

    [Theory]
    [InlineData("0")]
    [InlineData("1000")]
    [InlineData("dos")]
    [InlineData("1.5")]
    public void Los_bultos_fuera_de_rango_se_rechazan(string bultos) =>
        Assert.Contains("entre 1 y 999", Assert.Single(Validar(Fila(bultos: bultos)).Errores));

    [Theory]
    [InlineData("mañana")]
    [InlineData("31/02/2026")]
    [InlineData("10/05")]
    public void Una_fecha_que_no_se_entiende_se_rechaza(string fecha) =>
        Assert.Contains("no se entiende", Assert.Single(Validar(Fila(fecha: fecha)).Errores));

    [Fact]
    public void Una_fecha_fuera_de_la_ventana_se_rechaza() =>
        Assert.Contains("no puede superar", Assert.Single(Validar(Fila(fecha: "05/10/2028")).Errores));

    [Fact]
    public void Pasado_el_corte_una_fila_para_manana_se_rechaza()
    {
        var fila = Fila(fecha: "03/10/2026");

        Assert.Empty(Validar(fila, new TimeOnly(17, 0)).Errores);
        Assert.Contains("La carga para mañana cerró", Assert.Single(Validar(fila, new TimeOnly(19, 0)).Errores));
    }

    [Theory]
    [InlineData("san justo")]
    [InlineData("SAN JUSTO ")]
    [InlineData("Sán Justo")]
    public void La_localidad_se_busca_sin_mayusculas_ni_acentos(string localidad) =>
        Assert.Equal(1, Validar(Fila(localidad: localidad)).Pedido!.LocalidadId);

    [Fact]
    public void Una_localidad_desconocida_se_rechaza() =>
        Assert.Equal("La localidad «Narnia» no está cargada en el sistema.", Assert.Single(Validar(Fila(localidad: "Narnia")).Errores));

    [Fact]
    public void Una_localidad_repetida_pide_el_partido()
    {
        var error = Assert.Single(Validar(Fila(localidad: "San Martin")).Errores);

        Assert.Equal("Hay más de una localidad «San Martin»: completá la columna Partido (General San Martín, Otro Partido).", error);
    }

    [Fact]
    public void El_partido_desempata_una_localidad_repetida() =>
        Assert.Equal(3, Validar(Fila(localidad: "San Martin", partido: "otro partido")).Pedido!.LocalidadId);

    [Fact]
    public void Una_localidad_sin_zona_se_rechaza() =>
        Assert.Contains("no tiene zona asignada", Assert.Single(Validar(Fila(localidad: "Sin Zona")).Errores));

    [Theory]
    [InlineData("tal vez")]
    [InlineData("2")]
    public void Un_urgente_que_no_es_si_o_no_se_rechaza(string urgente) =>
        Assert.Contains("usá sí o no", Assert.Single(Validar(Fila(urgente: urgente)).Errores));

    [Theory]
    [InlineData("-1")]
    [InlineData("pesado")]
    public void Un_peso_invalido_se_rechaza(string peso) =>
        Assert.Contains("El peso", Assert.Single(Validar(Fila(peso: peso)).Errores));

    [Fact]
    public void La_clave_de_duplicado_ignora_mayusculas_acentos_y_espacios() =>
        Assert.Equal(
            ImportacionPedidos.ClaveDuplicado("Juan Pérez", "Av. Mitre 1234", 1, Hoy),
            ImportacionPedidos.ClaveDuplicado(" juan perez", "av mitre  1234", 1, Hoy));

    // ---- Lectura del Excel ----

    private static MemoryStream Planilla(Action<IXLWorksheet> armar)
    {
        using var libro = new XLWorkbook();
        armar(libro.AddWorksheet("Pedidos"));
        var salida = new MemoryStream();
        libro.SaveAs(salida);
        salida.Position = 0;
        return salida;
    }

    private static void Encabezados(IXLWorksheet hoja, params string[] titulos)
    {
        for (var i = 0; i < titulos.Length; i++) hoja.Cell(1, i + 1).Value = titulos[i];
    }

    [Fact]
    public void La_planilla_se_lee_por_nombre_de_columna_en_cualquier_orden()
    {
        using var archivo = Planilla(hoja =>
        {
            Encabezados(hoja, "LOCALIDAD", "telefono", "Destinatario", "Fecha de entrega", "calle y numero", "Bultos", "Columna ajena");
            hoja.Cell(2, 1).Value = "San Justo";
            hoja.Cell(2, 2).Value = 1155551234;                    // teléfono guardado como número
            hoja.Cell(2, 3).Value = "Juan Pérez";
            hoja.Cell(2, 4).Value = new DateTime(2026, 10, 5);     // celda con formato de fecha
            hoja.Cell(2, 5).Value = "Av. Mitre 1234";
            hoja.Cell(2, 6).Value = 3;
            hoja.Cell(2, 7).Value = "se ignora";
            hoja.Cell(4, 3).Value = "Ana Gómez";                   // la fila 3 queda en blanco
        });

        var lectura = PlanillaPedidos.Leer(archivo);

        Assert.Null(lectura.Error);
        Assert.Equal([2, 4], lectura.Filas.Select(f => f.Numero));
        var fila = lectura.Filas[0];
        Assert.Equal("Juan Pérez", fila.Destinatario);
        Assert.Equal("1155551234", fila.Telefono);
        Assert.Equal("2026-10-05", fila.FechaEntrega);
        Assert.Equal("3", fila.Bultos);
        Assert.Empty(Validar(fila).Errores);
    }

    [Fact]
    public void Una_planilla_sin_las_columnas_obligatorias_dice_cuales_faltan()
    {
        using var archivo = Planilla(hoja =>
        {
            Encabezados(hoja, "Destinatario", "Localidad");
            hoja.Cell(2, 1).Value = "Juan Pérez";
        });

        var lectura = PlanillaPedidos.Leer(archivo);

        Assert.Equal(
            "A la planilla le faltan columnas: Teléfono, Calle y número, Fecha de entrega. Descargá la plantilla para ver el formato.",
            lectura.Error);
    }

    [Fact]
    public void Un_archivo_que_no_es_excel_se_rechaza()
    {
        using var archivo = new MemoryStream("destinatario;telefono\nJuan;1155551234"u8.ToArray());

        Assert.Equal("El archivo no es una planilla Excel (.xlsx) válida.", PlanillaPedidos.Leer(archivo).Error);
    }

    [Fact]
    public void Una_planilla_con_mas_filas_que_el_tope_se_rechaza()
    {
        using var archivo = Planilla(hoja =>
        {
            Encabezados(hoja, "Destinatario", "Teléfono", "Calle y número", "Localidad", "Fecha de entrega");
            for (var n = 2; n <= ImportacionPedidos.MaxFilas + 2; n++) hoja.Cell(n, 1).Value = $"Destinatario {n}";
        });

        Assert.Contains("más de 500 pedidos", PlanillaPedidos.Leer(archivo).Error);
    }

    [Fact]
    public void La_plantilla_trae_los_encabezados_y_ningun_pedido_de_ejemplo()
    {
        using var archivo = new MemoryStream(PlanillaPedidos.Plantilla(new DateOnly(2026, 10, 5)));

        Assert.Equal("La planilla no tiene filas para importar.", PlanillaPedidos.Leer(archivo).Error);
    }
}
