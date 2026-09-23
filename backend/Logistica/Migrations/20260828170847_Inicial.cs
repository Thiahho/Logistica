using System;
using Logistica.Entidades;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Logistica.Migrations
{
    /// <inheritdoc />
    public partial class Inicial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:Enum:estado_pedido", "borrador,confirmado,en_ruta,entregado,fallido,reprogramado,devuelto,cancelado");

            migrationBuilder.CreateTable(
                name: "clientes",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    razon_social = table.Column<string>(type: "text", nullable: false),
                    cuit = table.Column<string>(type: "text", nullable: true),
                    contacto = table.Column<string>(type: "text", nullable: true),
                    telefono = table.Column<string>(type: "text", nullable: true),
                    email = table.Column<string>(type: "text", nullable: true),
                    color_pago = table.Column<string>(type: "text", nullable: false, defaultValue: "rojo"),
                    color_trato = table.Column<string>(type: "text", nullable: false, defaultValue: "amarillo"),
                    color_oper = table.Column<string>(type: "text", nullable: false, defaultValue: "amarillo"),
                    activo = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_clientes", x => x.id);
                    table.CheckConstraint("ck_clientes_color_oper", "color_oper in ('verde','amarillo','rojo')");
                    table.CheckConstraint("ck_clientes_color_pago", "color_pago in ('verde','amarillo','rojo')");
                    table.CheckConstraint("ck_clientes_color_trato", "color_trato in ('verde','amarillo','rojo')");
                });

            migrationBuilder.CreateTable(
                name: "tipos_evento_cliente",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    codigo = table.Column<string>(type: "text", nullable: false),
                    dimension = table.Column<string>(type: "text", nullable: false),
                    descripcion = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tipos_evento_cliente", x => x.id);
                    table.CheckConstraint("ck_tipos_evento_cliente_dimension", "dimension in ('pago','trato','operacion')");
                });

            migrationBuilder.CreateTable(
                name: "zonas",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    codigo = table.Column<string>(type: "character varying(1)", maxLength: 1, nullable: false),
                    nombre = table.Column<string>(type: "text", nullable: false),
                    activa = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_zonas", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "usuarios",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    nombre = table.Column<string>(type: "text", nullable: false),
                    email = table.Column<string>(type: "text", nullable: false),
                    password_hash = table.Column<string>(type: "text", nullable: false),
                    rol = table.Column<string>(type: "text", nullable: false),
                    cliente_id = table.Column<int>(type: "integer", nullable: true),
                    activo = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_usuarios", x => x.id);
                    table.CheckConstraint("ck_usuarios_cliente_coherente", "rol <> 'cliente' or cliente_id is not null");
                    table.CheckConstraint("ck_usuarios_rol", "rol in ('administracion','operacion','repartidor','cliente')");
                    table.ForeignKey(
                        name: "FK_usuarios_clientes_cliente_id",
                        column: x => x.cliente_id,
                        principalTable: "clientes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "localidades",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    nombre = table.Column<string>(type: "text", nullable: false),
                    partido = table.Column<string>(type: "text", nullable: true),
                    cp = table.Column<string>(type: "text", nullable: true),
                    zona_id = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_localidades", x => x.id);
                    table.ForeignKey(
                        name: "FK_localidades_zonas_zona_id",
                        column: x => x.zona_id,
                        principalTable: "zonas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "tarifas",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    cliente_id = table.Column<int>(type: "integer", nullable: true),
                    zona_id = table.Column<int>(type: "integer", nullable: false),
                    precio = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    vigente_desde = table.Column<DateOnly>(type: "date", nullable: false, defaultValueSql: "current_date"),
                    vigente_hasta = table.Column<DateOnly>(type: "date", nullable: true),
                    creada_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tarifas", x => x.id);
                    table.CheckConstraint("ck_tarifas_precio", "precio > 0");
                    table.CheckConstraint("ck_tarifas_vigencia", "vigente_hasta is null or vigente_hasta >= vigente_desde");
                    table.ForeignKey(
                        name: "FK_tarifas_clientes_cliente_id",
                        column: x => x.cliente_id,
                        principalTable: "clientes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_tarifas_zonas_zona_id",
                        column: x => x.zona_id,
                        principalTable: "zonas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "refresh_tokens",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "text", nullable: false),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    expira_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revocado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    reemplazado_por_id = table.Column<Guid>(type: "uuid", nullable: true),
                    creado_por_ip = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_refresh_tokens", x => x.id);
                    table.ForeignKey(
                        name: "FK_refresh_tokens_refresh_tokens_reemplazado_por_id",
                        column: x => x.reemplazado_por_id,
                        principalTable: "refresh_tokens",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_refresh_tokens_usuarios_usuario_id",
                        column: x => x.usuario_id,
                        principalTable: "usuarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "rutas",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    fecha = table.Column<DateOnly>(type: "date", nullable: false),
                    repartidor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    vehiculo = table.Column<string>(type: "text", nullable: true),
                    capacidad_paradas = table.Column<int>(type: "integer", nullable: false, defaultValue: 24),
                    estado = table.Column<string>(type: "text", nullable: false, defaultValue: "planificada"),
                    km_inicial = table.Column<int>(type: "integer", nullable: true),
                    km_final = table.Column<int>(type: "integer", nullable: true),
                    combustible_monto = table.Column<decimal>(type: "numeric(12,2)", nullable: true),
                    peajes_monto = table.Column<decimal>(type: "numeric(12,2)", nullable: true),
                    otros_costos = table.Column<decimal>(type: "numeric(12,2)", nullable: true),
                    pago_repartidor = table.Column<decimal>(type: "numeric(12,2)", nullable: true),
                    notas_cierre = table.Column<string>(type: "text", nullable: true),
                    cerrada_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    creada_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rutas", x => x.id);
                    table.CheckConstraint("ck_rutas_estado", "estado in ('planificada','en_curso','cerrada')");
                    table.ForeignKey(
                        name: "FK_rutas_usuarios_repartidor_id",
                        column: x => x.repartidor_id,
                        principalTable: "usuarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ubicaciones",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    calle_numero = table.Column<string>(type: "text", nullable: false),
                    localidad_id = table.Column<int>(type: "integer", nullable: true),
                    referencia = table.Column<string>(type: "text", nullable: true),
                    lat = table.Column<decimal>(type: "numeric(10,7)", nullable: true),
                    lng = table.Column<decimal>(type: "numeric(10,7)", nullable: true),
                    geo_confianza = table.Column<string>(type: "text", nullable: true),
                    geo_proveedor = table.Column<string>(type: "text", nullable: true),
                    geo_fecha = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    verificada = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    creada_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ubicaciones", x => x.id);
                    table.CheckConstraint("ck_ubicaciones_geo_confianza", "geo_confianza in ('alta','media','baja','fallida')");
                    table.ForeignKey(
                        name: "FK_ubicaciones_localidades_localidad_id",
                        column: x => x.localidad_id,
                        principalTable: "localidades",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "pedidos",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    cliente_id = table.Column<int>(type: "integer", nullable: false),
                    referencia_cliente = table.Column<string>(type: "text", nullable: true),
                    tipo = table.Column<string>(type: "text", nullable: false, defaultValue: "entrega"),
                    pedido_origen_id = table.Column<long>(type: "bigint", nullable: true),
                    origen_ubicacion_id = table.Column<long>(type: "bigint", nullable: false),
                    destino_ubicacion_id = table.Column<long>(type: "bigint", nullable: false),
                    destinatario_nombre = table.Column<string>(type: "text", nullable: false),
                    destinatario_telefono = table.Column<string>(type: "text", nullable: false),
                    bultos = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    peso_kg = table.Column<decimal>(type: "numeric(8,2)", nullable: true),
                    valor_declarado = table.Column<decimal>(type: "numeric(12,2)", nullable: true),
                    fecha_entrega = table.Column<DateOnly>(type: "date", nullable: false),
                    urgente = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    zona_id = table.Column<int>(type: "integer", nullable: true),
                    precio_base = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    recargo_urgencia = table.Column<decimal>(type: "numeric(12,2)", nullable: false, defaultValue: 0m),
                    descuento_ruta = table.Column<decimal>(type: "numeric(12,2)", nullable: false, defaultValue: 0m),
                    peajes = table.Column<decimal>(type: "numeric(12,2)", nullable: false, defaultValue: 0m),
                    total = table.Column<decimal>(type: "numeric(12,2)", nullable: false),
                    precio_congelado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    estado = table.Column<EstadoPedido>(type: "estado_pedido", nullable: false, defaultValue: EstadoPedido.Borrador),
                    origen_carga = table.Column<string>(type: "text", nullable: false, defaultValue: "interno"),
                    observaciones = table.Column<string>(type: "text", nullable: true),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pedidos", x => x.id);
                    table.CheckConstraint("ck_pedidos_bultos", "bultos > 0");
                    table.CheckConstraint("ck_pedidos_origen_carga", "origen_carga in ('interno','importado','portal','api')");
                    table.CheckConstraint("ck_pedidos_origen_coherente", "tipo = 'entrega' or pedido_origen_id is not null");
                    table.CheckConstraint("ck_pedidos_tipo", "tipo in ('entrega','retorno','reintento')");
                    table.ForeignKey(
                        name: "FK_pedidos_clientes_cliente_id",
                        column: x => x.cliente_id,
                        principalTable: "clientes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_pedidos_pedidos_pedido_origen_id",
                        column: x => x.pedido_origen_id,
                        principalTable: "pedidos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_pedidos_ubicaciones_destino_ubicacion_id",
                        column: x => x.destino_ubicacion_id,
                        principalTable: "ubicaciones",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_pedidos_ubicaciones_origen_ubicacion_id",
                        column: x => x.origen_ubicacion_id,
                        principalTable: "ubicaciones",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_pedidos_zonas_zona_id",
                        column: x => x.zona_id,
                        principalTable: "zonas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ruta_paradas",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ruta_id = table.Column<long>(type: "bigint", nullable: false),
                    ubicacion_id = table.Column<long>(type: "bigint", nullable: false),
                    tipo = table.Column<string>(type: "text", nullable: false),
                    orden = table.Column<int>(type: "integer", nullable: false),
                    anclada = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    estado = table.Column<string>(type: "text", nullable: false, defaultValue: "pendiente"),
                    llegada_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    salida_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ruta_paradas", x => x.id);
                    table.CheckConstraint("ck_ruta_paradas_estado", "estado in ('pendiente','completada','fallida')");
                    table.CheckConstraint("ck_ruta_paradas_tipo", "tipo in ('retiro','entrega','deposito')");
                    table.ForeignKey(
                        name: "FK_ruta_paradas_rutas_ruta_id",
                        column: x => x.ruta_id,
                        principalTable: "rutas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ruta_paradas_ubicaciones_ubicacion_id",
                        column: x => x.ubicacion_id,
                        principalTable: "ubicaciones",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "eventos_cliente",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    cliente_id = table.Column<int>(type: "integer", nullable: false),
                    tipo_id = table.Column<int>(type: "integer", nullable: false),
                    pedido_id = table.Column<long>(type: "bigint", nullable: true),
                    valor_num = table.Column<decimal>(type: "numeric(12,2)", nullable: true),
                    nota = table.Column<string>(type: "text", nullable: true),
                    ocurrido_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    registrado_por = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_eventos_cliente", x => x.id);
                    table.ForeignKey(
                        name: "FK_eventos_cliente_clientes_cliente_id",
                        column: x => x.cliente_id,
                        principalTable: "clientes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_eventos_cliente_pedidos_pedido_id",
                        column: x => x.pedido_id,
                        principalTable: "pedidos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_eventos_cliente_tipos_evento_cliente_tipo_id",
                        column: x => x.tipo_id,
                        principalTable: "tipos_evento_cliente",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_eventos_cliente_usuarios_registrado_por",
                        column: x => x.registrado_por,
                        principalTable: "usuarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "pedido_eventos",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    pedido_id = table.Column<long>(type: "bigint", nullable: false),
                    estado_anterior = table.Column<EstadoPedido>(type: "estado_pedido", nullable: true),
                    estado_nuevo = table.Column<EstadoPedido>(type: "estado_pedido", nullable: false),
                    motivo = table.Column<string>(type: "text", nullable: true),
                    actor_tipo = table.Column<string>(type: "text", nullable: false),
                    actor_usuario_id = table.Column<Guid>(type: "uuid", nullable: true),
                    actor_texto = table.Column<string>(type: "text", nullable: true),
                    ocurrido_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pedido_eventos", x => x.id);
                    table.CheckConstraint("ck_pedido_eventos_actor_identificado", "actor_tipo = 'sistema' or actor_usuario_id is not null");
                    table.CheckConstraint("ck_pedido_eventos_actor_tipo", "actor_tipo in ('sistema','usuario')");
                    table.ForeignKey(
                        name: "FK_pedido_eventos_pedidos_pedido_id",
                        column: x => x.pedido_id,
                        principalTable: "pedidos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_pedido_eventos_usuarios_actor_usuario_id",
                        column: x => x.actor_usuario_id,
                        principalTable: "usuarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "parada_pedidos",
                columns: table => new
                {
                    parada_id = table.Column<long>(type: "bigint", nullable: false),
                    pedido_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_parada_pedidos", x => new { x.parada_id, x.pedido_id });
                    table.ForeignKey(
                        name: "FK_parada_pedidos_pedidos_pedido_id",
                        column: x => x.pedido_id,
                        principalTable: "pedidos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_parada_pedidos_ruta_paradas_parada_id",
                        column: x => x.parada_id,
                        principalTable: "ruta_paradas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "pruebas_entrega",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    pedido_id = table.Column<long>(type: "bigint", nullable: false),
                    parada_id = table.Column<long>(type: "bigint", nullable: true),
                    resultado = table.Column<string>(type: "text", nullable: false),
                    motivo_fallo = table.Column<string>(type: "text", nullable: true),
                    receptor_nombre = table.Column<string>(type: "text", nullable: true),
                    identidad_verificada = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    foto_path = table.Column<string>(type: "text", nullable: true),
                    lat = table.Column<decimal>(type: "numeric(10,7)", nullable: true),
                    lng = table.Column<decimal>(type: "numeric(10,7)", nullable: true),
                    desvio_metros = table.Column<int>(type: "integer", nullable: true),
                    capturada_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    sincronizada_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    device_uuid = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pruebas_entrega", x => x.id);
                    table.CheckConstraint("ck_pruebas_entrega_resultado", "resultado in ('entregado','fallido')");
                    table.ForeignKey(
                        name: "FK_pruebas_entrega_pedidos_pedido_id",
                        column: x => x.pedido_id,
                        principalTable: "pedidos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_pruebas_entrega_ruta_paradas_parada_id",
                        column: x => x.parada_id,
                        principalTable: "ruta_paradas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_eventos_cliente_cliente_id_ocurrido_en",
                table: "eventos_cliente",
                columns: new[] { "cliente_id", "ocurrido_en" });

            migrationBuilder.CreateIndex(
                name: "IX_eventos_cliente_pedido_id",
                table: "eventos_cliente",
                column: "pedido_id");

            migrationBuilder.CreateIndex(
                name: "IX_eventos_cliente_registrado_por",
                table: "eventos_cliente",
                column: "registrado_por");

            migrationBuilder.CreateIndex(
                name: "IX_eventos_cliente_tipo_id",
                table: "eventos_cliente",
                column: "tipo_id");

            migrationBuilder.CreateIndex(
                name: "IX_localidades_nombre_partido",
                table: "localidades",
                columns: new[] { "nombre", "partido" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_localidades_zona_id",
                table: "localidades",
                column: "zona_id");

            migrationBuilder.CreateIndex(
                name: "IX_parada_pedidos_pedido_id",
                table: "parada_pedidos",
                column: "pedido_id");

            migrationBuilder.CreateIndex(
                name: "IX_pedido_eventos_actor_usuario_id",
                table: "pedido_eventos",
                column: "actor_usuario_id");

            migrationBuilder.CreateIndex(
                name: "IX_pedido_eventos_pedido_id_ocurrido_en",
                table: "pedido_eventos",
                columns: new[] { "pedido_id", "ocurrido_en" });

            migrationBuilder.CreateIndex(
                name: "IX_pedidos_cliente_id_fecha_entrega",
                table: "pedidos",
                columns: new[] { "cliente_id", "fecha_entrega" });

            migrationBuilder.CreateIndex(
                name: "IX_pedidos_destino_ubicacion_id",
                table: "pedidos",
                column: "destino_ubicacion_id");

            migrationBuilder.CreateIndex(
                name: "IX_pedidos_fecha_entrega_estado",
                table: "pedidos",
                columns: new[] { "fecha_entrega", "estado" });

            migrationBuilder.CreateIndex(
                name: "IX_pedidos_origen_ubicacion_id",
                table: "pedidos",
                column: "origen_ubicacion_id");

            migrationBuilder.CreateIndex(
                name: "IX_pedidos_pedido_origen_id",
                table: "pedidos",
                column: "pedido_origen_id");

            migrationBuilder.CreateIndex(
                name: "IX_pedidos_zona_id",
                table: "pedidos",
                column: "zona_id");

            migrationBuilder.CreateIndex(
                name: "IX_pruebas_entrega_capturada_en",
                table: "pruebas_entrega",
                column: "capturada_en");

            migrationBuilder.CreateIndex(
                name: "IX_pruebas_entrega_parada_id",
                table: "pruebas_entrega",
                column: "parada_id");

            migrationBuilder.CreateIndex(
                name: "IX_pruebas_entrega_pedido_id_device_uuid",
                table: "pruebas_entrega",
                columns: new[] { "pedido_id", "device_uuid" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_refresh_tokens_reemplazado_por_id",
                table: "refresh_tokens",
                column: "reemplazado_por_id");

            migrationBuilder.CreateIndex(
                name: "IX_refresh_tokens_token_hash",
                table: "refresh_tokens",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_refresh_tokens_usuario_id",
                table: "refresh_tokens",
                column: "usuario_id");

            migrationBuilder.CreateIndex(
                name: "IX_ruta_paradas_ruta_id",
                table: "ruta_paradas",
                column: "ruta_id");

            migrationBuilder.CreateIndex(
                name: "IX_ruta_paradas_ubicacion_id",
                table: "ruta_paradas",
                column: "ubicacion_id");

            migrationBuilder.CreateIndex(
                name: "IX_rutas_fecha",
                table: "rutas",
                column: "fecha");

            migrationBuilder.CreateIndex(
                name: "IX_rutas_repartidor_id",
                table: "rutas",
                column: "repartidor_id");

            migrationBuilder.CreateIndex(
                name: "IX_tarifas_cliente_id_zona_id",
                table: "tarifas",
                columns: new[] { "cliente_id", "zona_id" },
                filter: "vigente_hasta is null");

            migrationBuilder.CreateIndex(
                name: "IX_tarifas_zona_id",
                table: "tarifas",
                column: "zona_id");

            migrationBuilder.CreateIndex(
                name: "IX_tipos_evento_cliente_codigo",
                table: "tipos_evento_cliente",
                column: "codigo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ubicaciones_localidad_id",
                table: "ubicaciones",
                column: "localidad_id");

            migrationBuilder.CreateIndex(
                name: "IX_usuarios_cliente_id",
                table: "usuarios",
                column: "cliente_id");

            migrationBuilder.CreateIndex(
                name: "IX_usuarios_email",
                table: "usuarios",
                column: "email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_zonas_codigo",
                table: "zonas",
                column: "codigo",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "eventos_cliente");

            migrationBuilder.DropTable(
                name: "parada_pedidos");

            migrationBuilder.DropTable(
                name: "pedido_eventos");

            migrationBuilder.DropTable(
                name: "pruebas_entrega");

            migrationBuilder.DropTable(
                name: "refresh_tokens");

            migrationBuilder.DropTable(
                name: "tarifas");

            migrationBuilder.DropTable(
                name: "tipos_evento_cliente");

            migrationBuilder.DropTable(
                name: "pedidos");

            migrationBuilder.DropTable(
                name: "ruta_paradas");

            migrationBuilder.DropTable(
                name: "rutas");

            migrationBuilder.DropTable(
                name: "ubicaciones");

            migrationBuilder.DropTable(
                name: "usuarios");

            migrationBuilder.DropTable(
                name: "localidades");

            migrationBuilder.DropTable(
                name: "clientes");

            migrationBuilder.DropTable(
                name: "zonas");
        }
    }
}
