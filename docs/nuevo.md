Auditoría completa del sistema — BF Transportes / Sistema de Gestión Logística

Fecha: 02/10/2026 Base auditada: acta_sistema.md v4.36 · construccion_v1.md v1.55 · estado_implementacion.md (pasada del 02/10/2026) · auditoria_seguridad.md (última actualización 23/09/2026) · todo.md (18/09/2026) Rama de referencia: main (desde el merge de demo-d, 23/09/2026)

Límite de esta auditoría, dicho primero. Se hizo sobre la documentación, no sobre el código. Toda la evidencia de "verificado" que aparece acá es la que declararon las propias sesiones de construcción. No es una auditoría independiente: confirma qué dice la documentación que existe y dónde la documentación se contradice a sí misma. Para pasar de "documentado" a "auditado", ver §A.11 (verificación independiente).

Escala de evidencia usada en todo el documento

Nivel	Significa
V-Nav	Verificado por API y recorrido en navegador (Chrome/Chromium headless)
V-API	Verificado por API (curl/psql) o pruebas unitarias; pantallas no recorridas
Compila	Pasa tsc, eslint, next build; sin prueba funcional
Doc	Solo descripto en documentación; sin evidencia de prueba
—	No existe
PARTE A — INFORME TÉCNICO (uso interno)
A.1 Inventario cuantitativo
Elemento	Cantidad	Cómo se contó
Controladores API	26	ls Controllers/*.cs
Endpoints ([Http*])	182	grep reproducible documentado en estado_implementacion.md §1
Pantallas (page.tsx)	49	find frontend/app -name page.tsx
Entidades / tablas núcleo	31 / 28	32 archivos en Entidades/ (incluye el enum EstadoPedido)
Migraciones	26	Inicial → AgregarViajes
Pruebas automatizadas	100 (xUnit)	Solo lógica pura de Dominio/ y middleware
Roles	4 de autorización (administración, operación, repartidor, cliente) + subrol de cliente (dueño/empleado)	Políticas en Program.cs
Triggers de negocio	≥ 10	Congelamiento de precio, logs inmutables, facturas/pagos, declaración del repartidor, novedades, liquidaciones, rutas liquidadas, historial de rangos, actividad del portal, dirección dudosa
A.2 Módulos — estado por etapa contractual (Anexo I §5)
Etapa	Contenido	Estado	Evidencia
Base (H0/H1/H3)	Auth, pedidos, tarificación, rutas, cierre económico, exportación	Construido	V-API
E0	Nomenclatura, pisos numéricos, B9 precio manual	Cerrada (acta 4.1)	V-API
E1	Cuenta corriente, facturación, pagos FIFO, corte por deuda, B16 ajustes	Cerrada (acta 4.2)	V-API
E2	B4 liquidación repartidor, B3 rangos de cliente, B7 costos fijos y rentabilidad	Cerrada (acta 4.23)	V-Nav
E3	B2 tablero de indicadores	Cerrada (acta 4.24)	V-Nav
E4	B5 portal de carga, B13 recepción ✔ · B6 notificaciones ✘ · B11 importación masiva ✘	Parcial	B5/B13 V-API
E5	B10 PWA offline (cola, service worker, manifest)	Sin construir	—
Detalle módulo por módulo
Módulo	Controladores	Estado	Evidencia	Observación crítica
Autenticación	AuthController	OK	V-API + V-Nav (4.36)	Refresh en cookie httpOnly, rate limit por IP real detrás del proxy
Geografía y zonas automáticas	Zonas, Localidades, Ubicaciones	OK	V-API (Nominatim/OSRM reales)	Hueco de 0–5 km en los datos cargados: el depósito queda sin zona hasta corregir
Link de Google Maps	EnlaceMapaService, DireccionDesdeMapaService	OK	V-API	Link corto maps.app.goo.gl nunca probado con un link real
Comercial (clientes, tarifas, eventos)	Clientes, Tarifas	OK	V-API	—
Pedidos y precio	Pedidos	OK	V-API	Precio calculado solo en servidor; congelado por trigger
Planificación de rutas	Rutas	OK	V-API	Sin log de eventos de ruta: reasignar y reordenar no dejan rastro (gap aceptado)
Urgencias en ruta (RF-45)	Rutas	OK	V-Nav	Ventana y tope provisionales
Deliverys D14 + recargo km	Deliverys	OK	V-API	Recargo km = 0 hasta cargar TramosKm
Ejecución en calle	MisParadas, MiJornada, PruebasEntrega	OK sin offline	V-API; solo /hoy→/hoy/cierre en navegador	Nunca probado en un teléfono real
Novedades de la calle	MiJornada, Novedades	OK	V-API	Panel en navegador no verificado
Cierre en dos actores	Rutas.Cerrar, MiJornada	OK	V-API	Trigger de inmutabilidad probado
Cuenta corriente / facturación	Facturas, Clientes	OK	V-API	Cierre de ciclo manual, sin scheduler
Cobranza y avisos	Clientes	OK	V-API	Email por Resend; WhatsApp solo link manual
Liquidación repartidor	Liquidaciones	OK	V-Nav	Emite comprobante, no paga
Rangos de cliente	Rangos	OK	V-Nav	Sin umbrales cargados: nadie sube de rango
Rentabilidad	Rentabilidad	OK	V-Nav	Sin estructura objetivo cargada
Tablero	Tablero	OK	V-Nav (datos sintéticos)	Sin comparación entre períodos, sin NPS
Portal cliente (carga, Mi plan, contactos)	MiCuenta	OK	V-API	Mayoría de pantallas sin navegador
Portal dueño/empleados, Mi negocio, pago informado	MiCuenta, Clientes	OK	V-API	Pantallas no recorridas en navegador (pasada 25/09)
Viajes multi-parada	Viajes, MiCuenta	OK	V-API	Precio del viaje sin definir (provisorio: suma de envíos)
Recepción en depósito	Pedidos	OK	V-API	—
Repartidores / Jornada	Repartidores, Jornada	OK	V-API	Sin ausencias declaradas (vacaciones = "libre")
Exportación CSV	Exportar	OK	V-API	Máx. 366 días, escape anti CSV-injection
Monitoreo	middleware	OK	V-API (modo Production)	Monitor externo no conectado
A.3 Validaciones
Capa	Qué valida	Estado
Filtro global (FiltroLimitesDeTexto)	Texto ≤ 500 car. (2.000 libre, 2.048 URL, 200 contraseñas), listas ≤ 5.000	OK
Body	Tope 5 MB; multipart con tope propio (3 MB cierre, 2 MB comprobante, 1 MB novedad)	OK
Alta de pedido	Bultos 1–999, peso, valor declarado, nombre y teléfono obligatorios, ventana de fechas (portal hoy→90 días; interno −30→+365)	OK — 38/38 pruebas
Datos de cliente	Email, CUIT con dígito verificador, teléfono (Dominio/Validaciones.cs)	OK (1.40)
Contraseñas	≥ 10 caracteres, letras y números, sin email, no común	OK (1.40) — las existentes no se fuerzan a cambiar
DNI del receptor	6–9 dígitos o motivo ≥ 3 caracteres	OK
Numéricos	[Range] en montos, km, capacidad; km final ≥ inicial	OK
Zonas	Rangos de km sin solapamiento (semiabierto); huecos avisan sin bloquear	OK
Reglas de negocio en base	Trigger, no aplicación (regla 3 de construccion_v1.md)	OK — diseño correcto
Paginación	100 por página, 500 sin paginar	OK

Hallazgo de validación abierto: RegistrarPago no acepta un contraasiento negativo. No existe mecanismo para corregir un pago cargado por error (ni nota de crédito). En desarrollo ya quedó un pago de prueba de $1.400 irreversible. En producción, un error de tipeo de Administración no tiene corrección limpia. Severidad: Alta para operar con dinero real.

A.4 Pruebas (tests)
Tipo	Existe	Cobertura
Unitarias backend (xUnit)	Sí — 100	Lógica pura de Dominio/: precio, rangos, rentabilidad, liquidación, urgencias, equipo, negocio, orden de paradas, IP del proxy, mapeo ORS
Integración backend contra base	No	Se verificó a mano con curl/psql sobre copias de la base
Frontend (unitarias o E2E)	No	Recorridos manuales en Chromium headless en algunas pasadas
Prueba en teléfono real	No	Ninguna pantalla del repartidor probada en un dispositivo
Carga / rendimiento	Sí, una vez	100.000 pedidos, 100 usuarios simultáneos (23/09)
CI	Sí	GitHub Actions: build con warnings-as-errors, tests, typecheck, lint, build frontend
Protección de main en GitHub	No activada	Un push que rompe CI puede llegar a producción

Riesgo principal de testing: la verificación de los 182 endpoints es manual y no reproducible. Cada cambio futuro depende de que alguien repita los curl. Los triggers de base (núcleo de la integridad del sistema) no tienen ninguna prueba automatizada.

A.5 Seguridad
Resuelto (con evidencia)
#	Hallazgo	Estado
1	Fuerza bruta en login	Token bucket por IP real (detrás del proxy, con secreto compartido)
3	Formato email/CUIT/teléfono	Resuelto 1.40
4	Política de contraseñas	Resuelto 1.40
6	Rate limit en avisos masivos	Resuelto 1.40
7	Paginación sin tope (DoS)	Resuelto
8	Valores absurdos en carga	Resuelto
9	Cabeceras de seguridad + CSP con nonce	Resuelto 1.40, 0 violaciones en 10 pantallas
10	Rate limit en servicios externos	Resuelto
11	Exportes sin límite	Resuelto
12	Swagger roto	Resuelto
14–16	IP detrás del proxy, fotos en disco efímero, contenedor root	Resueltos en código (1.34)
—	JWT manipulado, CORS, SQL injection, matriz de permisos, aislamiento entre clientes, IDOR	Probados sin hallazgos (23/09)
—	Nombres de personal interno expuestos al cliente	Cerrado (4.10)
Abierto
#	Hallazgo	Severidad	Acción
2	Contraseña de Postgres de desarrollo en el historial de git	Media	Usar contraseña distinta en producción (obligatorio)
13	2 vulnerabilidades moderadas en qs vía shadcn (herramienta de desarrollo)	Baja	Revisar al actualizar
17	Procedimiento de migración y alta del primer administrador en producción	Alta (bloquea go-live)	Ver A.8
—	Rate limit de login por IP: varios operadores detrás del mismo router comparten cupo	Baja	Aceptado
—	Sin auditoría de acciones sobre rutas (reasignar, reordenar)	Media	Gap aceptado por techo de tablas
—	DNI del receptor almacenado sin dictamen legal	Media-legal	Consulta legal §11.2
—	Fotos con datos personales procesadas por Cloudinary fuera del país	Media-legal	Misma consulta legal
—	Sin respaldo diario con restauración probada (RNF-10)	Alta	Configurar y probar restauración antes del go-live
—	Protección de rama main no activada	Media	Activar en GitHub + "deploy after CI" en Render

Desactualización documental crítica: auditoria_seguridad.md no refleja los cierres de 1.40 (hallazgos 3, 4, 6, 9) y no contiene los hallazgos 14 a 18, aunque construccion_v1.md y el acta los citan por número. El documento de seguridad está incompleto respecto de su propia numeración.

A.6 Contradicciones entre documentos

Estas no son cosméticas: el acta es el documento contractual de referencia ("toda discusión de alcance se resuelve contra este texto"). Si el acta dice una cosa y el sistema hace otra, el cliente puede reclamar contra el acta.

#	Contradicción	Dónde	Riesgo
C1	Modo offline: el acta lo declara "requisito de la primera versión, no mejora posterior" (RNF-01) y es criterio de aceptación 3. La decisión del 24/09 lo pasó a funcionalidad futura, presupuestada aparte. El acta no registra esa decisión.	Acta §6, §8, §9.1	Alto — contractual. Hoy el criterio 3 no se puede cumplir
C2	§2 dice que el sistema no almacena imagen del documento; RF-23 (reescrito en 4.7) dice que sí	Acta §2 vs §5.3	Medio
C3	§4 dice "nueve tablas núcleo" y que las tablas de E2 están "sin construir todavía" — hay 28 y E2 está cerrada	Acta §4	Bajo
C4	construccion_v1.md §4.2 y §9 describen endpoints y configuración de la foto del documento y su purga como si existieran; estado_implementacion.md confirma que no hay código	construccion §4.2, §9, §10	Medio — confunde a quien mantenga
C5	construccion_v1.md §8 marca H2 "Sin construir"; §4.2 dice que las pantallas están construidas	construccion §8 vs §4.2	Bajo
C6	construccion_v1.md §1 (Hosting) dice que el reenvío es por next.config.ts; desde 1.34 es proxy.ts	construccion §1	Bajo
C7	construccion_v1.md §2 (estructura del repo) y §4.1 ("Mis envíos: solo lectura") quedaron en el estado de agosto	construccion §2, §4.1	Bajo
C8	estado_implementacion.md §5 dice "22 entidades" y "22 migraciones" en el encabezado del listado; §1 dice 31 y 26	estado §5	Bajo
C9	estado_implementacion.md §7 cita el acta v4.31 como fuente; la vigente es 4.36	estado §7	Bajo
C10	schema_v3.sql desactualizado desde antes de E1	docs/	Medio — no sirve como referencia de DDL
C11	§9.2 del acta pone "Liquidación al repartidor" detrás del disparador "segundo repartidor"; se construyó por §9.3 sin que §9.2 lo aclare	Acta §9.2	Bajo
A.7 Lo que NO está construido
Pieza	Referencia	Impacto
Cola offline de la PWA (service worker, manifest, Dexie, sincronización diferida)	B10 / RNF-01 / RF-25 / E5	Sin señal en la calle, la captura falla. Siete caminos de escritura a envolver
Notificaciones automáticas por cambio de estado	B6 / E4	El cliente no recibe avisos; debe entrar al portal
Importación masiva de pedidos	B11 / E4	Clientes con volumen deben cargar uno por uno
Cierre automático de carga a las 18:00	RF-08	No hay scheduler en el proyecto; se cierra por disciplina, no por sistema
Cierre automático de ciclo de facturación	E1	Se dispara a mano (recuperable si se atrasa)
Imagen del documento con retención y purga	RF-23 reescrito	Bloqueado por consulta legal; hoy solo número de DNI
Corrección de pagos / notas de crédito	E1	Ver A.3
Log de eventos de ruta	§4.3 construcción	Gap aceptado
Ausencias de repartidor	RF-34	Vacaciones figuran como "libre"
Comparación entre períodos, prorrateo de costos, NPS	B2 / B7	El tablero muestra un período aislado
Asignación masiva parada-vehículo	§10.1	Disparador: tercer vehículo
Consolidación en depósito propio	§10.2	Previsión, sin construir
Logo real sobre fondo claro	Identidad visual	Se usa un wordmark provisorio
A.8 Información faltante para terminar
A.8.1 Parámetros comerciales que la Empresa debe cargar (sin ellos el sistema funciona pero no calcula)
Parámetro	Dónde	Valor hoy	Efecto si queda así
Tarifas por zona (lista general y por cliente)	/tarifas, ficha cliente	Desarrollo	Sin tarifa, todo pedido requiere precio manual
Rangos de km por zona (empezando en 0 km)	/tarifas	Hueco 0–5 km	Localidades cercanas al depósito sin zona
Depósito real	/depositos	Vacío en producción	Sin depósito no hay origen ni zonas automáticas
Precio:FactorUrgencia	config	0,20 provisional	El Anexo preveía 0,40–0,60
Precio:FactorDescuentoRuta	config	0,10 provisional	—
Precio:TramosKm (recargo por km)	config	Vacío	Ningún delivery cobra recargo por km
Precio de un viaje multi-parada	Decisión pendiente	Suma de envíos	Tarifa provisoria
Portal:HoraCorte	config	16:00 provisional	—
Ventana de urgencias y tope	config	13:00, 60 min, 2 paradas	—
Motivos de entrega fallida	config	Provisionales	—
Categorías de novedades	config	Provisionales	—
Capacidad de paradas por vehículo	/vehiculos	24 por defecto	—
Parámetros de liquidación (por entrega, bono, % mínimo, motivos imputables)	/tarifas	Vacíos	Pago al repartidor se tipea a mano
Umbrales y descuentos de rangos	/tarifas	Vacíos	Nadie sube de rango
Costos fijos mensuales y estructura objetivo	/rentabilidad	Vacíos	Rentabilidad sin comparación
Portal:MostrarPrecios	config	Apagado	Confirmar que todos los clientes son por suscripción
A.8.2 Decisiones legales (acta §11.2 y derivadas)
Configuración contractual del repartidor.
Límite de responsabilidad por bulto.
Tratamiento de datos del destinatario: DNI almacenado, libreta "Mis clientes", fotos en Cloudinary (fuera del país).
Retención de mercadería ante corte de servicio por deuda (§10.2-L2).
Plazo de retención de la imagen del documento (si se habilita).
A.8.3 Decisiones operativas abiertas (acta §11.2)
Fecha de última salida del fundador como acompañante.
Plazo de entrega comprometido al cliente.
Plan de contingencia por ausencia del repartidor.
RF-08: los borradores del día siguiente a las 18:00, ¿se cancelan o se postergan?
A.8.4 Infraestructura para el go-live (checklist construccion_v1.md §10)
 Proveedor de Postgres de producción (debe admitir pg_trgm y triggers)
 Base migrada antes de arrancar el backend (o reiniciar después: Npgsql cachea el catálogo de tipos)
 Primer administrador creado a mano, con contraseña nueva
 Cuenta Cloudinary + variables en Render
 Cuenta OpenRouteService + variables en Render
 PROXY_SECRETO idéntico en Vercel y Render, Proxy__Habilitado=true
 Jwt__Key, ConnectionStrings__Postgres, Frontend__Origin, Resend__ApiKey
 Dominio propio verificado en Resend (el remitente de prueba solo entrega a la cuenta dueña)
 Respaldo diario + una restauración probada (RNF-10)
 Monitor externo sobre /health/listo
 Protección de main y deploy condicionado a CI
 Plan de Render: el gratuito duerme a los 15 min
 Revisar si algún pedido del portal ya facturado sufrió el recargo duplicado (acta 4.22)
 Datos de prueba en base de desarrollo (pedidos TEST-CICLO, pago de $1.400, viaje ruta #6): confirmar que no viajan a producción
A.9 Criterios de aceptación del acta (§8)
#	Criterio	Estado	Nota
1	Pedido cargado en < 30 s	No medido	Nunca cronometrado con un operador real
2	Ruta armada en < 15 min	No medido	Ídem
3	Prueba de modo avión	Falla — no existe la cola	Ver contradicción C1
4	Resultado económico el mismo día	Cumple	V-API
5	Historia completa de estados	Cumple	Por trigger
6	Dirección inexistente bloqueada	Cumple	Por trigger
7	Precio confirmado inmodificable	Cumple	Por trigger
8	Tres rutas reales con resultado positivo	Pendiente	No se resuelve programando
A.10 Deuda técnica priorizada
Prioridad	Ítem
P0	Registrar en el acta la decisión de offline como futuro (C1); corregir criterio 3
P0	Mecanismo de corrección de pagos (contraasiento o nota de crédito)
P0	Respaldo con restauración probada
P0	Prueba de la PWA del repartidor en un teléfono real (RNF-06, firma en canvas)
P1	Pruebas de integración de los triggers y del flujo de precio
P1	Actualizar auditoria_seguridad.md (hallazgos 14–18, cierres de 1.40)
P1	Regenerar schema_v3.sql con pg_dump --schema-only
P1	Limpiar contradicciones C2–C11
P2	Scheduler para RF-08 y cierre de ciclo (hoy manual)
P2	Log de eventos de ruta
A.11 Verificación independiente recomendada

Para que esta auditoría deje de depender de la palabra de quien construyó:

Recontar los conteos de A.1 con los comandos documentados, sobre main actual.
Correr dotnet test y el CI en limpio.
Matriz de permisos automatizada sobre los 182 endpoints (hoy se verificaron 125 el 23/09; 57 endpoints posteriores no pasaron esa matriz completa).
Recorrer en un teléfono Android real: retiro con firma → paradas → cierre de jornada.
Recorrer en navegador las pantallas marcadas "no verificadas": portal del dueño, Mi negocio, viajes, panel de novedades.