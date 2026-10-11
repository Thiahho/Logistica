using System.Globalization;
using System.Text;
using Logistica.Web;

namespace Logistica.Dominio;

/// <summary>Una fila de la planilla tal como vino, celda por celda, sin interpretar. `Numero` es el
/// número de fila en el Excel, para que el mensaje de error se pueda ubicar en el archivo.</summary>
public record FilaPlanilla(
    int Numero, string? Destinatario, string? Telefono, string? CalleNumero, string? Localidad, string? Partido,
    string? ReferenciaDireccion, string? Bultos, string? FechaEntrega, string? Observaciones,
    string? ReferenciaCliente, string? PesoKg, string? ValorDeclarado, string? Urgente);

public record LocalidadImportable(int Id, string Nombre, string? Partido, bool TieneZona);

/// <summary>La fila ya interpretada: lo que hace falta para dar de alta el pedido.</summary>
public record PedidoImportable(
    string DestinatarioNombre, string DestinatarioTelefono, string CalleNumero, int LocalidadId, string LocalidadNombre,
    string? ReferenciaDireccion, int Bultos, DateOnly FechaEntrega, string? Observaciones, string? ReferenciaCliente,
    decimal? PesoKg, decimal? ValorDeclarado, bool Urgente);

public record FilaValidada(int Numero, PedidoImportable? Pedido, IReadOnlyList<string> Errores);

/// <summary>
/// B11 (Anexo I §5, E4): importación masiva de pedidos desde una planilla. Acá vive la interpretación
/// de una fila — las mismas reglas que el alta de a uno (PedidosController.Crear): teléfono obligatorio
/// (RF-07), bultos 1–999, ventana de fechas del back-office y corte de carga (RF-08). Lógica pura: se
/// prueba sola en Logistica.Tests. Leer el Excel es de Servicios/PlanillaPedidos.cs.
/// </summary>
public static class ImportacionPedidos
{
    public const int MaxFilas = 500;

    private const int MaxNombre = 200;
    private const int MaxDireccion = 200;
    private const int MaxReferencia = 500;
    private const int MaxObservaciones = 2000;

    private static readonly string[] FormatosFecha = ["yyyy-MM-dd", "d/M/yyyy", "d-M-yyyy", "d/M/yy"];
    private static readonly HashSet<string> Si = ["si", "s", "1", "true", "verdadero", "x"];
    private static readonly HashSet<string> No = ["no", "n", "0", "false", "falso"];

    /// <summary>Minúsculas, sin acentos y solo letras y números: "Calle y Número" y "calle_y_numero"
    /// son la misma columna; "San Martín" y "san martin", la misma localidad.</summary>
    public static string Normalizar(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return "";
        var sb = new StringBuilder();
        foreach (var c in texto.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }

    /// <summary>Dos filas con la misma clave son, para la calle, el mismo envío: mismo destinatario,
    /// misma dirección, mismo día.</summary>
    public static string ClaveDuplicado(string destinatario, string calleNumero, int localidadId, DateOnly fechaEntrega) =>
        $"{Normalizar(destinatario)}|{Normalizar(calleNumero)}|{localidadId}|{fechaEntrega:yyyyMMdd}";

    public static FilaValidada Validar(
        FilaPlanilla fila, DateOnly hoy, TimeOnly ahora, TimeOnly horaCorte, IReadOnlyCollection<LocalidadImportable> localidades)
    {
        var errores = new List<string>();

        var destinatario = Texto(fila.Destinatario);
        if (destinatario is null) errores.Add("Falta el destinatario.");
        else if (destinatario.Length > MaxNombre) errores.Add($"El destinatario supera los {MaxNombre} caracteres.");

        var telefono = Texto(fila.Telefono);
        if (telefono is null) errores.Add("Falta el teléfono.");
        else if (!Validaciones.TelefonoValido(telefono)) errores.Add($"El teléfono «{telefono}» no es válido (entre 8 y 15 dígitos).");

        var calle = Texto(fila.CalleNumero);
        if (calle is null) errores.Add("Falta la calle y número.");
        else if (calle.Length > MaxDireccion) errores.Add($"La calle y número supera los {MaxDireccion} caracteres.");

        var localidad = ResolverLocalidad(Texto(fila.Localidad), Texto(fila.Partido), localidades, errores);

        var bultos = 1;
        if (Texto(fila.Bultos) is { } textoBultos
            && (!int.TryParse(textoBultos, NumberStyles.Integer, CultureInfo.InvariantCulture, out bultos) || bultos is < 1 or > 999))
            errores.Add($"Los bultos «{textoBultos}» deben ser un número entre 1 y 999.");

        var fecha = default(DateOnly);
        var textoFecha = Texto(fila.FechaEntrega);
        if (textoFecha is null) errores.Add("Falta la fecha de entrega.");
        else if (!DateOnly.TryParseExact(textoFecha, FormatosFecha, CultureInfo.InvariantCulture, DateTimeStyles.None, out fecha))
            errores.Add($"La fecha de entrega «{textoFecha}» no se entiende: usá día/mes/año.");
        else if (ValidacionFechas.FechaEntrega(fecha, hoy, diasAtras: 30, diasAdelante: 365) is { } errorFecha)
            errores.Add(errorFecha);
        else if (CorteDeCarga.Cerrada(fecha, hoy, ahora, horaCorte))
            errores.Add(CorteDeCarga.Mensaje(horaCorte, CorteDeCarga.PrimeraFechaAbierta(hoy, ahora, horaCorte)));

        var peso = Decimal(fila.PesoKg, "El peso", 0, 100_000, errores);
        var valor = Decimal(fila.ValorDeclarado, "El valor declarado", 0, 1_000_000_000, errores);

        var urgente = false;
        if (Texto(fila.Urgente) is { } textoUrgente)
        {
            var normalizado = Normalizar(textoUrgente);
            if (Si.Contains(normalizado)) urgente = true;
            else if (!No.Contains(normalizado)) errores.Add($"Urgente «{textoUrgente}» no se entiende: usá sí o no.");
        }

        var referenciaDireccion = Texto(fila.ReferenciaDireccion);
        if (referenciaDireccion?.Length > MaxReferencia) errores.Add($"La referencia de la dirección supera los {MaxReferencia} caracteres.");
        var referenciaCliente = Texto(fila.ReferenciaCliente);
        if (referenciaCliente?.Length > MaxReferencia) errores.Add($"La referencia del cliente supera los {MaxReferencia} caracteres.");
        var observaciones = Texto(fila.Observaciones);
        if (observaciones?.Length > MaxObservaciones) errores.Add($"Las observaciones superan los {MaxObservaciones} caracteres.");

        if (errores.Count > 0) return new FilaValidada(fila.Numero, null, errores);

        return new FilaValidada(fila.Numero, new PedidoImportable(
            destinatario!, telefono!, calle!, localidad!.Id, localidad.Nombre, referenciaDireccion, bultos, fecha,
            observaciones, referenciaCliente, peso, valor, urgente), errores);
    }

    private static LocalidadImportable? ResolverLocalidad(
        string? nombre, string? partido, IReadOnlyCollection<LocalidadImportable> localidades, List<string> errores)
    {
        if (nombre is null)
        {
            errores.Add("Falta la localidad.");
            return null;
        }

        var clave = Normalizar(nombre);
        var candidatas = localidades.Where(l => Normalizar(l.Nombre) == clave).ToList();
        if (candidatas.Count > 1 && partido is not null)
        {
            var clavePartido = Normalizar(partido);
            candidatas = candidatas.Where(l => Normalizar(l.Partido) == clavePartido).ToList();
        }

        switch (candidatas.Count)
        {
            case 0:
                errores.Add(partido is null
                    ? $"La localidad «{nombre}» no está cargada en el sistema."
                    : $"La localidad «{nombre}» ({partido}) no está cargada en el sistema.");
                return null;
            case > 1:
                var partidos = string.Join(", ", candidatas.Select(l => l.Partido ?? "sin partido").Order());
                errores.Add($"Hay más de una localidad «{nombre}»: completá la columna Partido ({partidos}).");
                return null;
        }

        var unica = candidatas[0];
        if (!unica.TieneZona)
        {
            errores.Add($"La localidad «{unica.Nombre}» no tiene zona asignada; no se puede cotizar.");
            return null;
        }
        return unica;
    }

    private static string? Texto(string? celda) => string.IsNullOrWhiteSpace(celda) ? null : celda.Trim();

    /// <summary>Acepta coma o punto decimal: "12,5" es lo que tipea alguien en una planilla en es-AR.</summary>
    private static decimal? Decimal(string? celda, string nombre, decimal minimo, decimal maximo, List<string> errores)
    {
        if (Texto(celda) is not { } texto) return null;
        var invariante = texto.Contains(',') && !texto.Contains('.') ? texto.Replace(',', '.') : texto;
        if (decimal.TryParse(invariante, NumberStyles.Number, CultureInfo.InvariantCulture, out var valor)
            && valor >= minimo && valor <= maximo)
            return valor;
        errores.Add($"{nombre} «{texto}» debe ser un número entre {minimo:0} y {maximo:0}.");
        return null;
    }
}
