using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Mvc;

namespace Logistica.Web;

/// <summary>
/// Escritor CSV propio, sin dependencia nueva: separador ';' (planilla en es-AR), comillas dobles
/// escapadas duplicándolas solo cuando el valor las necesita, y BOM UTF-8. Extraído de
/// ExportarController (RF-30) para reusarlo en el comprobante de liquidación (RF-41).
/// </summary>
public static class Csv
{
    public static FileContentResult Archivo(string contenido, string nombre)
    {
        // BOM UTF-8: sin esto Excel en Windows interpreta acentos como caracteres sueltos.
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(contenido)).ToArray();
        return new FileContentResult(bytes, "text/csv") { FileDownloadName = $"{nombre}.csv" };
    }

    public static string Escribir(string[] encabezados, IEnumerable<object?[]> filas)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(';', encabezados));
        foreach (var fila in filas)
            sb.AppendLine(string.Join(';', fila.Select(EscaparCampo)));
        return sb.ToString();
    }

    // Campos de texto libre (destinatario, razón social) terminan en un CSV que un admin abre en
    // Excel/Sheets: si empiezan con =, +, -, @ el programa los interpreta como fórmula (CSV
    // injection). Anteponer un apóstrofo neutraliza esa interpretación sin alterar el dato.
    private static readonly char[] PrefijosFormula = ['=', '+', '-', '@'];

    private static string EscaparCampo(object? valor)
    {
        var texto = valor switch
        {
            null => "",
            DateOnly d => d.ToString("yyyy-MM-dd"),
            DateTimeOffset dt => dt.ToString("O"),
            decimal dec => dec.ToString(CultureInfo.InvariantCulture),
            _ => valor.ToString() ?? "",
        };
        if (texto.Length > 0 && PrefijosFormula.Contains(texto[0]))
            texto = "'" + texto;

        return texto.Contains(';') || texto.Contains('"') || texto.Contains('\n')
            ? $"\"{texto.Replace("\"", "\"\"")}\""
            : texto;
    }
}
