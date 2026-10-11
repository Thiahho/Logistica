using System.Globalization;
using System.IO.Compression;
using ClosedXML.Excel;
using Logistica.Dominio;

namespace Logistica.Servicios;

/// <summary>
/// Lee y arma la planilla Excel de la importación masiva (B11). Solo traduce celdas a texto: qué es
/// válido lo decide Dominio/ImportacionPedidos.cs. Toma la primera hoja; la primera fila son los
/// encabezados, en cualquier orden y sin importar mayúsculas, acentos ni espacios.
/// </summary>
public static class PlanillaPedidos
{
    public const string TipoContenido = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private record Columna(string Titulo, bool Obligatoria, string Ejemplo, params string[] Alias);

    private static readonly Columna[] Columnas =
    [
        new("Destinatario", true, "Juan Pérez", "nombre", "destinatarionombre"),
        new("Teléfono", true, "11 5555-1234", "tel", "celular", "destinatariotelefono"),
        new("Calle y número", true, "Av. San Martín 1234", "direccion", "domicilio", "callenumero"),
        new("Localidad", true, "San Justo"),
        new("Partido", false, "La Matanza"),
        new("Referencia de la dirección", false, "Portón verde", "referenciadireccion", "entrecalles"),
        new("Bultos", false, "1", "cantidad"),
        new("Fecha de entrega", true, "", "fecha", "fechaentrega"),
        new("Observaciones", false, "Tocar timbre", "observacion", "notas"),
        new("Referencia del cliente", false, "Pedido 4512", "referencia", "referenciacliente", "nropedido"),
        new("Peso (kg)", false, "", "peso", "pesokg"),
        new("Valor declarado", false, "", "valor"),
        new("Urgente", false, "no"),
    ];

    public record Lectura(List<FilaPlanilla> Filas, string? Error);

    private const long MaxBytesDescomprimidos = 20 * 1024 * 1024;

    /// <param name="archivo">Tiene que admitir Seek (un MemoryStream con la subida ya copiada).</param>
    public static Lectura Leer(Stream archivo)
    {
        XLWorkbook libro;
        try
        {
            // Un .xlsx es un zip: uno de 1 MB puede declarar gigas al descomprimirse. Se mira el tamaño
            // real antes de dárselo a la librería, que carga la planilla entera en memoria.
            using (var zip = new ZipArchive(archivo, ZipArchiveMode.Read, leaveOpen: true))
            {
                if (zip.Entries.Sum(e => e.Length) > MaxBytesDescomprimidos)
                    return new Lectura([], "La planilla es demasiado grande.");
            }
            archivo.Position = 0;
            libro = new XLWorkbook(archivo);
        }
        catch (Exception)
        {
            return new Lectura([], "El archivo no es una planilla Excel (.xlsx) válida.");
        }

        using (libro)
        {
            var hoja = libro.Worksheets.FirstOrDefault();
            var ultima = hoja?.LastRowUsed()?.RowNumber() ?? 0;
            if (hoja is null || ultima < 2) return new Lectura([], "La planilla no tiene filas para importar.");

            var posiciones = new Dictionary<string, int>();
            foreach (var celda in hoja.Row(1).CellsUsed())
            {
                var titulo = ImportacionPedidos.Normalizar(celda.GetString());
                var columna = Columnas.FirstOrDefault(c =>
                    ImportacionPedidos.Normalizar(c.Titulo) == titulo || c.Alias.Contains(titulo));
                if (columna is not null) posiciones.TryAdd(columna.Titulo, celda.Address.ColumnNumber);
            }

            var faltantes = Columnas.Where(c => c.Obligatoria && !posiciones.ContainsKey(c.Titulo)).Select(c => c.Titulo).ToList();
            if (faltantes.Count > 0)
                return new Lectura([], $"A la planilla le faltan columnas: {string.Join(", ", faltantes)}. Descargá la plantilla para ver el formato.");

            var filas = new List<FilaPlanilla>();
            for (var n = 2; n <= ultima; n++)
            {
                var fila = hoja.Row(n);
                string? Celda(string titulo) =>
                    posiciones.TryGetValue(titulo, out var columna) ? Texto(fila.Cell(columna)) : null;

                var leida = new FilaPlanilla(
                    n, Celda("Destinatario"), Celda("Teléfono"), Celda("Calle y número"), Celda("Localidad"), Celda("Partido"),
                    Celda("Referencia de la dirección"), Celda("Bultos"), Celda("Fecha de entrega"), Celda("Observaciones"),
                    Celda("Referencia del cliente"), Celda("Peso (kg)"), Celda("Valor declarado"), Celda("Urgente"));

                // Una fila en blanco en el medio (o formato arrastrado hasta abajo) no es un pedido.
                if (leida with { Numero = 0 } == new FilaPlanilla(0, null, null, null, null, null, null, null, null, null, null, null, null, null))
                    continue;

                filas.Add(leida);
                if (filas.Count > ImportacionPedidos.MaxFilas)
                    return new Lectura([], $"La planilla tiene más de {ImportacionPedidos.MaxFilas} pedidos: partila en archivos más chicos.");
            }

            return filas.Count == 0
                ? new Lectura([], "La planilla no tiene filas para importar.")
                : new Lectura(filas, null);
        }
    }

    /// <summary>El valor de la celda como lo escribiría una persona. Las fechas salen en ISO y los
    /// números sin formato de Excel: un teléfono guardado como número no puede llegar como "1,15E+09".</summary>
    private static string? Texto(IXLCell celda)
    {
        if (celda.IsEmpty()) return null;
        var texto = celda.DataType switch
        {
            XLDataType.DateTime => celda.GetDateTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            XLDataType.Number => ((decimal)celda.GetDouble()).ToString("0.####", CultureInfo.InvariantCulture),
            XLDataType.Boolean => celda.GetBoolean() ? "si" : "no",
            _ => celda.GetString(),
        };
        return string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();
    }

    public static byte[] Plantilla(DateOnly fechaEjemplo)
    {
        using var libro = new XLWorkbook();
        var hoja = libro.AddWorksheet("Pedidos");
        for (var i = 0; i < Columnas.Length; i++)
        {
            var columna = Columnas[i];
            var encabezado = hoja.Cell(1, i + 1);
            encabezado.Value = columna.Titulo;
            encabezado.Style.Font.Bold = true;
            if (columna.Obligatoria) encabezado.Style.Fill.BackgroundColor = XLColor.LightYellow;

            // Texto, no General: Excel le saca el cero inicial a un teléfono y convierte "1-2" en fecha.
            hoja.Column(i + 1).Style.NumberFormat.Format = "@";
            hoja.Column(i + 1).Width = Math.Max(14, columna.Titulo.Length + 4);
        }
        hoja.SheetView.FreezeRows(1);

        // El ejemplo va en otra hoja: en la primera se importaría como un pedido más.
        var ayuda = libro.AddWorksheet("Instrucciones");
        string[] titulos = ["Columna", "Obligatoria", "Ejemplo"];
        for (var i = 0; i < titulos.Length; i++)
        {
            ayuda.Cell(1, i + 1).Value = titulos[i];
            ayuda.Cell(1, i + 1).Style.Font.Bold = true;
        }
        for (var i = 0; i < Columnas.Length; i++)
        {
            var columna = Columnas[i];
            ayuda.Cell(i + 2, 1).Value = columna.Titulo;
            ayuda.Cell(i + 2, 2).Value = columna.Obligatoria ? "sí" : "no";
            ayuda.Cell(i + 2, 3).Value = columna.Titulo == "Fecha de entrega"
                ? fechaEjemplo.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)
                : columna.Ejemplo;
        }
        ayuda.Column(3).Style.NumberFormat.Format = "@";
        ayuda.Cell(Columnas.Length + 3, 1).Value =
            "Cargá un pedido por fila en la hoja Pedidos, desde la fila 2. Si hay dos localidades con el mismo nombre, completá Partido.";
        ayuda.Columns(1, 3).AdjustToContents(1, Columnas.Length + 1);

        using var salida = new MemoryStream();
        libro.SaveAs(salida);
        return salida.ToArray();
    }
}
