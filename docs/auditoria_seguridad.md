# Auditoría de seguridad y validaciones

Pedida por el usuario sobre el sistema completo (backend/Logistica + frontend). Última actualización: 14/09/2026 — refleja los hallazgos "Alto" ya resueltos, el cambio de algoritmo del rate limiter de login y la revisión del panel de Cobranza.

## Alto

1. **~~Sin rate limiting en `/api/auth/login`~~ — RESUELTO** (construccion_v1.md changelog 1.16). Sin límite de intentos, era fuerza bruta viable contra el login. `Program.cs` agrega `AddRateLimiter` con policy `"login"`, particionada por IP — no por email, para que nadie pueda bloquear la cuenta de otro mandando intentos fallidos a su nombre. `AuthController.Login` lleva `[EnableRateLimiting("login")]`. **Actualizado el 14/09/2026: el algoritmo pasó de `FixedWindowRateLimiter` a `TokenBucketRateLimiter`** (capacidad 5, recarga 1 token cada 12s). Motivo: fixed window resetea la ventana entera de golpe, así que permitía un doble-burst en el borde (5 intentos a los 0:59 + 5 más a los 1:01 = 10 reales en 2 segundos); token bucket recarga gradualmente y elimina ese caso. El rechazo (429) devuelve `ProblemDetails` con `Retry-After: 12` (antes 60, ajustado al nuevo ritmo de recarga). Verificado con curl: intentos 1-5 pasan, 6to en adelante 429 con `Retry-After: 12`, y a los ~13s ya deja pasar un intento más (y vuelve a bloquear el siguiente inmediato).

2. **`appsettings.Development.json` con la contraseña de Postgres en texto plano, committeada al repo.** Riesgo bajo mientras sea solo la DB local de desarrollo, pero es un hábito a cortar — ya usan `dotnet user-secrets` para `Jwt:Key`; falta aplicar el mismo criterio acá. **Pendiente.**

## Medio

3. **Sin validación de formato en `Email`/`Cuit`/`Telefono`** (`ClientesController.CrearClienteRequest`, `UsuariosController.CrearUsuarioRequest`). Se persisten tal cual, sin regex ni límite de longitud. No es una vulnerabilidad (EF parametriza todo), pero permite datos basura silenciosos. Bajo costo de arreglar con `[EmailAddress]`/`[Phone]` de `System.ComponentModel.DataAnnotations`. **Pendiente.**

4. **Política de contraseña mínima (8 caracteres, sin más)** (`UsuariosController.cs`, `ClientesController.cs`). Correcto que usen `PasswordHasher<T>` (fuerza real del hash), pero "8 caracteres, cualquier cosa" es débil para cuentas de administración. Aceptable para el estadio actual (cuentas creadas solo por un admin, no self-signup). **Pendiente.**

## Corregido durante la sesión (no estaba en el alcance original de la auditoría)

5. **`Borrador → Confirmado` era una transición manual válida** (construccion_v1.md changelog 1.15). Permitía confirmar (y por lo tanto rutear) un pedido con `precio_base`/`total` en `null`, violando la regla de que el precio se fija exclusivamente al cerrar la planificación de la ruta (`RutasController.CerrarPlanificacion`, changelog 3.11). `TransicionesPedido.Permitidas[Borrador]` pasa a `[Cancelado]` únicamente. Verificado que `POST /api/pedidos/{id}/estado` con `estadoNuevo=Confirmado` sobre un pedido en Borrador devuelve 400.

## Revisado — Panel de Cobranza y avisos de vencimiento (14/09/2026)

Superficie nueva: `GET /api/clientes/riesgo`, `POST /api/clientes/avisos/previsualizacion`, `POST /api/clientes/avisos`, `EmailService` (Resend), `Dominio/EnlaceWhatsApp.cs`. Revisado contra el mismo criterio que el resto de esta auditoría — no es una auditoría nueva completa, es la revisión de lo que se agregó.

**Sin hallazgos de severidad Alta o Media.**

- **RBAC verificado con curl real**, no solo por lectura de código: `operacion@logistica.local` recibe 403 en las tres rutas nuevas; solo `Administracion` puede listar riesgo o mandar avisos — coherente con RF-33 (los indicadores/deuda del cliente son de uso interno).
- **Sin superficie de inyección SQL**: `RiesgoAsync` es LINQ de punta a punta, cero `FromSqlRaw`/concatenación de strings.
- **El link de WhatsApp no tiene inyección de URL**: el mensaje se arma con `Uri.EscapeDataString`, nunca concatenación cruda — un `RazonSocial` con caracteres especiales no puede alterar el link resultante. El `<a target="_blank">` del frontend lleva `rel="noopener noreferrer"` (sin esto, la pestaña abierta podría manipular `window.opener` en la página de origen — reverse tabnabbing).
- **El email es texto plano, no HTML** (`EmailService` manda `text`, no `html`) — sin superficie de inyección de markup en el cuerpo del correo.
- **La `ApiKey` de Resend nunca llega al repo**: sigue el mismo patrón que `Jwt:Key`/`ConnectionStrings:Postgres` — `user-secrets` en desarrollo, variable de entorno en producción, nunca `appsettings.json`. Verificado que `appsettings.json` solo tiene el remitente (dato público) en esa sección.
- **El servidor decide qué se manda, nunca el cliente**: `AvisosCobranzaService` recalcula la categoría/monto de cada cliente contra `RiesgoAsync` en el momento del envío — un front modificado no puede forzar el envío de un mensaje distinto al que la deuda real justifica.

6. **Sin límite de tasa propio en `POST /api/clientes/avisos`** — el único guardarraíl es el tope de 100 ids por request más la policy `Administracion`. Una cuenta admin comprometida podría llamarlo en loop y agotar la cuota de envíos de la cuenta de Resend (o, en menor medida, generar ruido en `eventos_cliente`). Mismo nivel de exposición que cualquier otra acción de admin ya existente (crear eventos, registrar pagos) — no es una vulnerabilidad nueva de este endpoint en particular, pero el volumen de un envío masivo lo hace más costoso de abusar que la mayoría. **Pendiente, no bloqueante**: si se agrega, un rate-limit por usuario (no por IP, ya autenticado) sobre este endpoint específico sería la mitigación natural.

## Fortalezas confirmadas (sin cambios necesarios)

- **Manejo de archivos** (`AlmacenamientoFotos.cs`): valida que el `deviceUuid` sea un GUID real antes de usarlo en una ruta de disco (previene path traversal), valida tamaño máximo, y verifica el magic-byte real del JPEG en vez de confiar en el `Content-Type` declarado.
- **SQL sin superficie de injection**: todos los `SqlQuery`/`ExecuteSqlInterpolatedAsync` (PrecioService, TarifasController, ClientesController) usan interpolación parametrizada por EF, nunca concatenación de strings.
- **IDOR bien cubierto**: `PedidosController.Detalle` filtra por el `clienteId` del claim (un cliente no puede ver pedidos ajenos cambiando el id de la URL); `MisParadasController` filtra cada acción por `Ruta.RepartidorId` (un repartidor no puede tocar paradas de otra ruta).
- **JWT/cookies bien configurados**: clave de firma solo por `user-secrets`/variable de entorno (sin fallback hardcodeado, falla el arranque si falta), validaciones de issuer/audience/lifetime activas, cookie de refresh `HttpOnly` + `SameSite=Strict` + `Secure` condicionado a no-dev, CORS con origen único (no `AllowAnyOrigin`) más credenciales.
- **Manejo de excepciones sin fugas**: `ManejadorExcepciones` traduce excepciones de reglas de negocio a `ProblemDetails`; ningún 500 filtra stack trace, ni siquiera en desarrollo.
- **Frontend sin superficie de XSS**: sin `dangerouslySetInnerHTML` en código propio, React escapa todo por defecto.

## Balance

Para donde está el proyecto (desarrollo, sin usuarios reales todavía), el único hallazgo que se trató como bloqueante (rate limiting del login) ya está resuelto. Lo que queda pendiente es deuda razonable para esta etapa — no urgente, pero a tener en cuenta antes de sumar clientes reales o desplegar fuera de un entorno controlado.
