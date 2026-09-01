# Auditoría de seguridad y validaciones

Pedida por el usuario sobre el sistema completo (backend/Logistica + frontend). Última actualización: 31/08/2026 — refleja los hallazgos "Alto" ya resueltos.

## Alto

1. **~~Sin rate limiting en `/api/auth/login`~~ — RESUELTO** (construccion_v1.md changelog 1.16). Sin límite de intentos, era fuerza bruta viable contra el login. `Program.cs` agrega `AddRateLimiter` con policy `"login"` (`FixedWindowRateLimiter`, 5 intentos por minuto, particionada por IP — no por email, para que nadie pueda bloquear la cuenta de otro mandando intentos fallidos a su nombre). `AuthController.Login` lleva `[EnableRateLimiting("login")]`. El rechazo (429) devuelve `ProblemDetails` con `Retry-After: 60`. Verificado con curl: intentos 1-5 pasan, 6to en adelante 429, y pasado el minuto un login válido vuelve a funcionar.

2. **`appsettings.Development.json` con la contraseña de Postgres en texto plano, committeada al repo.** Riesgo bajo mientras sea solo la DB local de desarrollo, pero es un hábito a cortar — ya usan `dotnet user-secrets` para `Jwt:Key`; falta aplicar el mismo criterio acá. **Pendiente.**

## Medio

3. **Sin validación de formato en `Email`/`Cuit`/`Telefono`** (`ClientesController.CrearClienteRequest`, `UsuariosController.CrearUsuarioRequest`). Se persisten tal cual, sin regex ni límite de longitud. No es una vulnerabilidad (EF parametriza todo), pero permite datos basura silenciosos. Bajo costo de arreglar con `[EmailAddress]`/`[Phone]` de `System.ComponentModel.DataAnnotations`. **Pendiente.**

4. **Política de contraseña mínima (8 caracteres, sin más)** (`UsuariosController.cs`, `ClientesController.cs`). Correcto que usen `PasswordHasher<T>` (fuerza real del hash), pero "8 caracteres, cualquier cosa" es débil para cuentas de administración. Aceptable para el estadio actual (cuentas creadas solo por un admin, no self-signup). **Pendiente.**

## Corregido durante la sesión (no estaba en el alcance original de la auditoría)

5. **`Borrador → Confirmado` era una transición manual válida** (construccion_v1.md changelog 1.15). Permitía confirmar (y por lo tanto rutear) un pedido con `precio_base`/`total` en `null`, violando la regla de que el precio se fija exclusivamente al cerrar la planificación de la ruta (`RutasController.CerrarPlanificacion`, changelog 3.11). `TransicionesPedido.Permitidas[Borrador]` pasa a `[Cancelado]` únicamente. Verificado que `POST /api/pedidos/{id}/estado` con `estadoNuevo=Confirmado` sobre un pedido en Borrador devuelve 400.

## Fortalezas confirmadas (sin cambios necesarios)

- **Manejo de archivos** (`AlmacenamientoFotos.cs`): valida que el `deviceUuid` sea un GUID real antes de usarlo en una ruta de disco (previene path traversal), valida tamaño máximo, y verifica el magic-byte real del JPEG en vez de confiar en el `Content-Type` declarado.
- **SQL sin superficie de injection**: todos los `SqlQuery`/`ExecuteSqlInterpolatedAsync` (PrecioService, TarifasController, ClientesController) usan interpolación parametrizada por EF, nunca concatenación de strings.
- **IDOR bien cubierto**: `PedidosController.Detalle` filtra por el `clienteId` del claim (un cliente no puede ver pedidos ajenos cambiando el id de la URL); `MisParadasController` filtra cada acción por `Ruta.RepartidorId` (un repartidor no puede tocar paradas de otra ruta).
- **JWT/cookies bien configurados**: clave de firma solo por `user-secrets`/variable de entorno (sin fallback hardcodeado, falla el arranque si falta), validaciones de issuer/audience/lifetime activas, cookie de refresh `HttpOnly` + `SameSite=Strict` + `Secure` condicionado a no-dev, CORS con origen único (no `AllowAnyOrigin`) más credenciales.
- **Manejo de excepciones sin fugas**: `ManejadorExcepciones` traduce excepciones de reglas de negocio a `ProblemDetails`; ningún 500 filtra stack trace, ni siquiera en desarrollo.
- **Frontend sin superficie de XSS**: sin `dangerouslySetInnerHTML` en código propio, React escapa todo por defecto.

## Balance

Para donde está el proyecto (desarrollo, sin usuarios reales todavía), el único hallazgo que se trató como bloqueante (rate limiting del login) ya está resuelto. Lo que queda pendiente es deuda razonable para esta etapa — no urgente, pero a tener en cuenta antes de sumar clientes reales o desplegar fuera de un entorno controlado.
