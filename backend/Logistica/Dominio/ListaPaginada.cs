namespace Logistica.Dominio;

/// <summary>Envoltorio de página, reusado por cualquier listado con `pagina`/`tamanioPagina`
/// (PedidosController, RutasController). `Total` es la cantidad de filas que matchean el
/// filtro, no un importe — homónimo con campos `Total` de otros dominios por coincidencia de
/// nombre, no relación.</summary>
public record ListaPaginada<T>(IReadOnlyList<T> Items, int Total);
