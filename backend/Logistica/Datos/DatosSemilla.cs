using Logistica.Auth;
using Logistica.Entidades;
using Logistica.Opciones;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Logistica.Datos;

/// <summary>
/// Seed de desarrollo. `zonas` y `tipos_evento_cliente` son datos fijos del negocio (acta §4,
/// coinciden con el seed mínimo de schema_v3.sql). Todo lo demás —localidades, tarifas,
/// clientes y usuarios— son DATOS DE MUESTRA para poder probar el sistema localmente:
/// el área de cobertura real y las tarifas reales son decisión comercial pendiente
/// (construccion_v1.md §10, acta §13).
/// </summary>
public static class DatosSemilla
{
    public static async Task SembrarAsync(LogisticaDbContext db, OpcionesDeposito deposito, CancellationToken ct = default)
    {
        // Base recién creada = todavía no hay usuarios. La ruta de demostración de más abajo solo tiene
        // sentido ahí: en una base que ya se usó, "no hay rutas" significa que alguien las borró (limpieza
        // de datos de prueba), no que falte sembrarlas — recrearlas devolvía la demo a una base limpia
        // (y fallaba en cuanto había más de un repartidor).
        var esBaseNueva = !await db.Usuarios.AnyAsync(ct);

        if (!await db.Zonas.AnyAsync(ct))
        {
            db.Zonas.AddRange(
                new Zona { Codigo = "A", Nombre = "Cercana" },
                new Zona { Codigo = "B", Nombre = "Media" },
                new Zona { Codigo = "C", Nombre = "Lejana" },
                new Zona { Codigo = "D", Nombre = "Muy lejana" });
            await db.SaveChangesAsync(ct);
        }

        if (!await db.TiposEventoCliente.AnyAsync(ct))
        {
            db.TiposEventoCliente.AddRange(
                new TipoEventoCliente { Codigo = "pago_termino", Dimension = "pago", Descripcion = "Pago dentro del plazo" },
                new TipoEventoCliente { Codigo = "pago_tardio", Dimension = "pago", Descripcion = "Pago fuera de plazo (valor_num = días)" },
                new TipoEventoCliente { Codigo = "impago", Dimension = "pago", Descripcion = "Comprobante vencido sin pago" },
                new TipoEventoCliente { Codigo = "rechazo_injust", Dimension = "trato", Descripcion = "Rechazo injustificado del pedido" },
                new TipoEventoCliente { Codigo = "cambio_ruta_armada", Dimension = "trato", Descripcion = "Cambio con la ruta ya planificada" },
                new TipoEventoCliente { Codigo = "reclamo_desest", Dimension = "trato", Descripcion = "Reclamo desestimado" },
                new TipoEventoCliente { Codigo = "direccion_erronea", Dimension = "operacion", Descripcion = "Dirección incorrecta provista por el cliente" },
                new TipoEventoCliente { Codigo = "destinatario_ausente", Dimension = "operacion", Descripcion = "Destinatario ausente" },
                new TipoEventoCliente { Codigo = "entrega_ok", Dimension = "operacion", Descripcion = "Entrega sin incidente" },
                new TipoEventoCliente { Codigo = "reserva_anticipada", Dimension = "operacion", Descripcion = "Pedido cargado antes del corte" },
                // Panel de cobranza: acción del SISTEMA (un admin manda un aviso), no conducta
                // del cliente — distinto de 'impago'. Ver Migrations/…_AgregarTipoEventoAvisoCobranza.cs.
                new TipoEventoCliente { Codigo = "aviso_cobranza", Dimension = "pago", Descripcion = "Aviso de cobranza enviado al cliente" });
            await db.SaveChangesAsync(ct);
        }

        // ---- A partir de acá: datos de muestra para desarrollo local, no reales ----

        if (!await db.Localidades.AnyAsync(ct))
        {
            var zonaA = await db.Zonas.SingleAsync(z => z.Codigo == "A", ct);
            var zonaB = await db.Zonas.SingleAsync(z => z.Codigo == "B", ct);
            var zonaC = await db.Zonas.SingleAsync(z => z.Codigo == "C", ct);
            var zonaD = await db.Zonas.SingleAsync(z => z.Codigo == "D", ct);

            db.Localidades.AddRange(
                new Localidad { Nombre = "CABA", Partido = "Ciudad Autónoma de Buenos Aires", ZonaId = zonaA.Id },
                new Localidad { Nombre = "Vicente López", Partido = "Vicente López", ZonaId = zonaA.Id },
                new Localidad { Nombre = "San Isidro", Partido = "San Isidro", ZonaId = zonaB.Id },
                new Localidad { Nombre = "Tigre", Partido = "Tigre", ZonaId = zonaC.Id },
                new Localidad { Nombre = "Pilar", Partido = "Pilar", ZonaId = zonaD.Id });
            await db.SaveChangesAsync(ct);
        }

        // Catálogo de depósitos (acta changelog 3.8): sembramos uno solo, nombrado "Depósito" —
        // administración carga los demás desde /depositos cuando los necesite.
        if (!await db.Ubicaciones.AnyAsync(u => u.NombreDeposito != null, ct))
        {
            var localidadDeposito = await db.Localidades.SingleAsync(l => l.Nombre == deposito.Localidad, ct);
            db.Ubicaciones.Add(new Ubicacion
            {
                CalleNumero = deposito.CalleNumero,
                LocalidadId = localidadDeposito.Id,
                NombreDeposito = "Depósito",
                Lat = deposito.Lat,
                Lng = deposito.Lng,
                GeoConfianza = "alta",
                GeoProveedor = "semilla",
                GeoFecha = DateTimeOffset.UtcNow,
                Verificada = true,
            });
            await db.SaveChangesAsync(ct);
        }

        if (!await db.Clientes.AnyAsync(ct))
        {
            db.Clientes.AddRange(
                new Cliente
                {
                    RazonSocial = "Cliente Demo Uno S.A.",
                    ColorPago = "verde", ColorTrato = "verde", ColorOper = "verde",
                },
                new Cliente { RazonSocial = "Cliente Demo Dos S.R.L." },
                // E1: ciclo quincenal, para poder probar los dos ciclos de facturación
                // (§10.2-A/D2) sin editar la base a mano — el resto nace en "mensual" (default).
                new Cliente { RazonSocial = "Cliente Demo Tres (quincenal)", CicloFacturacion = "quincenal" });
            await db.SaveChangesAsync(ct);
        }

        if (!await db.Tarifas.AnyAsync(ct))
        {
            var zonas = await db.Zonas.ToListAsync(ct);
            var precioPorZona = new Dictionary<string, decimal>
            {
                ["A"] = 1500m, ["B"] = 2200m, ["C"] = 3000m, ["D"] = 4200m,
            };
            db.Tarifas.AddRange(zonas.Select(z => new Tarifa { ZonaId = z.Id, Precio = precioPorZona[z.Codigo] }));
            await db.SaveChangesAsync(ct);
        }

        if (!await db.Usuarios.AnyAsync(ct))
        {
            var hasher = new PasswordHasher<Usuario>();
            const string passwordDev = "Logistica123!"; // solo desarrollo local, nunca en producción

            Usuario Crear(string nombre, string email, string rol)
            {
                var usuario = new Usuario { Nombre = nombre, Email = email, Rol = rol };
                usuario.PasswordHash = hasher.HashPassword(usuario, passwordDev);
                return usuario;
            }

            db.Usuarios.AddRange(
                Crear("Admin Demo", "admin@logistica.local", Roles.Administracion),
                Crear("Operación Demo", "operacion@logistica.local", Roles.Operacion),
                Crear("Repartidor Demo", "repartidor@logistica.local", Roles.Repartidor));
            await db.SaveChangesAsync(ct);
        }

        // Login del cliente demo: tabla separada de Usuarios a propósito (ver Entidades/ClienteUsuario.cs).
        if (!await db.ClientesUsuarios.AnyAsync(ct))
        {
            const string passwordDev = "Logistica123!"; // solo desarrollo local, nunca en producción
            var clienteDemo = await db.Clientes.FirstAsync(ct);

            var usuarioCliente = new ClienteUsuario
            {
                ClienteId = clienteDemo.Id,
                Nombre = "Cliente Demo",
                Email = "cliente@logistica.local",
            };
            usuarioCliente.PasswordHash = AuthService.HashearCliente(usuarioCliente, passwordDev);

            db.ClientesUsuarios.Add(usuarioCliente);
            await db.SaveChangesAsync(ct);
        }

        if (!await db.Vehiculos.AnyAsync(ct))
        {
            db.Vehiculos.AddRange(
                new Vehiculo
                {
                    Patente = "AB123CD",
                    Descripcion = "Utilitario 1",
                    Marca = "Renault",
                    Modelo = "Kangoo",
                    Anio = 2020,
                    KmActual = 45000,
                    CapacidadParadas = 24,
                },
                // Inactivo a propósito: sirve para probar en desarrollo que /seleccion (el
                // desplegable de armar ruta) filtra por Activo.
                new Vehiculo
                {
                    Patente = "XY987ZW",
                    Descripcion = "Utilitario 2 (de baja)",
                    Marca = "Fiat",
                    Modelo = "Fiorino",
                    Anio = 2017,
                    KmActual = 98000,
                    CapacidadParadas = 20,
                    Activo = false,
                });
            await db.SaveChangesAsync(ct);
        }

        // Ruta de ayer, en curso y sin cerrar: sin esto el cierre económico (RF-26/27) no se
        // puede probar de punta a punta en desarrollo sin haber armado una ruta a mano primero
        // (el armado en sí es una fase posterior).
        if (esBaseNueva && !await db.Rutas.AnyAsync(ct))
        {
            var repartidor = await db.Usuarios.SingleAsync(u => u.Rol == Roles.Repartidor, ct);
            var vehiculo = await db.Vehiculos.SingleAsync(v => v.Patente == "AB123CD", ct);
            var clienteDemo = await db.Clientes.FirstAsync(ct);
            var zonaA = await db.Zonas.SingleAsync(z => z.Codigo == "A", ct);
            var caba = await db.Localidades.SingleAsync(l => l.Nombre == "CABA", ct);
            var vicenteLopez = await db.Localidades.SingleAsync(l => l.Nombre == "Vicente López", ct);
            var origenDeposito = await db.Ubicaciones.SingleAsync(u => u.NombreDeposito != null, ct);

            // Destinos ya verificados: no dependen del geocoder para poder cerrar la ruta
            // sembrada sin red (dato de desarrollo, no real).
            var destinos = Enumerable.Range(1, 4).Select(i => new Ubicacion
            {
                CalleNumero = $"Calle Demo {i * 100}",
                LocalidadId = i % 2 == 0 ? vicenteLopez.Id : caba.Id,
                Lat = -34.60m + i * 0.01m,
                Lng = -58.38m + i * 0.01m,
                GeoConfianza = "alta",
                GeoProveedor = "semilla",
                GeoFecha = DateTimeOffset.UtcNow,
                Verificada = true,
            }).ToList();
            db.Ubicaciones.AddRange(destinos);
            await db.SaveChangesAsync(ct);

            var ayer = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));

            Pedido NuevoPedido(Ubicacion destino, EstadoPedido estado) => new()
            {
                ClienteId = clienteDemo.Id,
                OrigenUbicacionId = origenDeposito.Id,
                DestinoUbicacionId = destino.Id,
                DestinatarioNombre = $"Destinatario {destino.CalleNumero}",
                DestinatarioTelefono = "1122334455",
                Bultos = 1,
                FechaEntrega = ayer,
                ZonaId = zonaA.Id,
                PrecioBase = 1500m,
                Total = 1500m,
                PrecioCongeladoEn = DateTimeOffset.UtcNow,
                Estado = estado,
                CreadoEn = DateTimeOffset.UtcNow,
            };

            var pedidos = new List<Pedido>
            {
                NuevoPedido(destinos[0], EstadoPedido.Entregado),
                NuevoPedido(destinos[1], EstadoPedido.Entregado),
                NuevoPedido(destinos[2], EstadoPedido.Entregado),
                NuevoPedido(destinos[3], EstadoPedido.Fallido),
            };
            db.Pedidos.AddRange(pedidos);
            await db.SaveChangesAsync(ct);

            var ruta = new Ruta
            {
                Fecha = ayer,
                RepartidorId = repartidor.Id,
                VehiculoId = vehiculo.Id,
                // Toda ruta "en_curso" tiene que tener origen resuelto (acta changelog 3.8,
                // CerrarPlanificacion lo exige) — esta se siembra directo en ese estado, sin pasar
                // por el endpoint, así que hay que replicar el invariante a mano.
                OrigenUbicacionId = origenDeposito.Id,
                CapacidadParadas = 24,
                Estado = "en_curso",
                CreadaEn = DateTimeOffset.UtcNow,
            };
            db.Rutas.Add(ruta);
            await db.SaveChangesAsync(ct);

            for (var i = 0; i < pedidos.Count; i++)
            {
                var parada = new RutaParada
                {
                    RutaId = ruta.Id,
                    UbicacionId = destinos[i].Id,
                    Tipo = "entrega",
                    Orden = i + 1,
                    Estado = pedidos[i].Estado == EstadoPedido.Entregado ? "completada" : "fallida",
                    LlegadaEn = DateTimeOffset.UtcNow.AddDays(-1),
                    SalidaEn = DateTimeOffset.UtcNow.AddDays(-1).AddMinutes(10),
                };
                db.RutaParadas.Add(parada);
                await db.SaveChangesAsync(ct); // necesita el Id generado antes de la fila puente

                db.ParadaPedidos.Add(new ParadaPedido { ParadaId = parada.Id, PedidoId = pedidos[i].Id });
            }
            await db.SaveChangesAsync(ct);
        }
    }
}
