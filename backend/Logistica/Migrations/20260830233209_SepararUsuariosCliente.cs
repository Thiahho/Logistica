using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Logistica.Migrations
{
    /// <summary>
    /// Separa los logins de cliente de `usuarios` (personal interno) a una tabla propia,
    /// `clientes_usuarios` — acta_sistema_v3.md §11.1 actualizado, motivo de negocio: los dos
    /// tipos de cuenta no deben compartir tabla ni gestión.
    ///
    /// Orden deliberado: primero se crea `clientes_usuarios` y se mueven ahí (con SQL crudo) las
    /// filas rol='cliente' de `usuarios`, junto con el borrado de sus refresh tokens (esas
    /// sesiones simplemente vuelven a loguearse) — recién después se dropean las columnas/checks
    /// viejos de `usuarios`. Si el DROP COLUMN fuera antes del movimiento de datos, el
    /// cliente_id de esas filas se perdería sin poder copiarlas.
    /// </summary>
    /// <inheritdoc />
    public partial class SepararUsuariosCliente : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "clientes_usuarios",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    cliente_id = table.Column<int>(type: "integer", nullable: false),
                    nombre = table.Column<string>(type: "text", nullable: false),
                    email = table.Column<string>(type: "text", nullable: false),
                    password_hash = table.Column<string>(type: "text", nullable: false),
                    activo = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_clientes_usuarios", x => x.id);
                    table.ForeignKey(
                        name: "FK_clientes_usuarios_clientes_cliente_id",
                        column: x => x.cliente_id,
                        principalTable: "clientes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            // Mover los usuarios rol='cliente' existentes a la tabla nueva, antes de tocar el
            // shape viejo de `usuarios`. ck_usuarios_cliente_coherente (todavía vigente acá)
            // garantiza que toda fila rol='cliente' ya tiene cliente_id no nulo.
            migrationBuilder.Sql(@"
insert into clientes_usuarios (id, cliente_id, nombre, email, password_hash, activo, creado_en)
select id, cliente_id, nombre, email, password_hash, activo, creado_en
from usuarios
where rol = 'cliente';");

            // Las sesiones de esos usuarios quedan huérfanas (la FK vieja igual las hubiera
            // dejado inválidas al borrar la fila) — se loguean de nuevo, no rompe nada.
            migrationBuilder.Sql(@"
delete from refresh_tokens
where usuario_id in (select id from usuarios where rol = 'cliente');");

            migrationBuilder.Sql("delete from usuarios where rol = 'cliente';");

            migrationBuilder.DropForeignKey(
                name: "FK_usuarios_clientes_cliente_id",
                table: "usuarios");

            migrationBuilder.DropIndex(
                name: "IX_usuarios_cliente_id",
                table: "usuarios");

            migrationBuilder.DropCheckConstraint(
                name: "ck_usuarios_cliente_coherente",
                table: "usuarios");

            migrationBuilder.DropCheckConstraint(
                name: "ck_usuarios_rol",
                table: "usuarios");

            migrationBuilder.DropColumn(
                name: "cliente_id",
                table: "usuarios");

            migrationBuilder.AlterColumn<Guid>(
                name: "usuario_id",
                table: "refresh_tokens",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "cliente_usuario_id",
                table: "refresh_tokens",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_usuarios_rol",
                table: "usuarios",
                sql: "rol in ('administracion','operacion','repartidor')");

            migrationBuilder.CreateIndex(
                name: "IX_refresh_tokens_cliente_usuario_id",
                table: "refresh_tokens",
                column: "cliente_usuario_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_refresh_tokens_actor_unico",
                table: "refresh_tokens",
                sql: "(usuario_id is not null and cliente_usuario_id is null) or (usuario_id is null and cliente_usuario_id is not null)");

            migrationBuilder.CreateIndex(
                name: "IX_clientes_usuarios_cliente_id",
                table: "clientes_usuarios",
                column: "cliente_id");

            migrationBuilder.CreateIndex(
                name: "IX_clientes_usuarios_email",
                table: "clientes_usuarios",
                column: "email",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_refresh_tokens_clientes_usuarios_cliente_usuario_id",
                table: "refresh_tokens",
                column: "cliente_usuario_id",
                principalTable: "clientes_usuarios",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            // Unicidad de email ENTRE usuarios y clientes_usuarios: un índice único de EF no
            // puede abarcar dos tablas, así que el login por email no ambiguo lo garantiza un
            // trigger en cada tabla que chequea la otra (construccion_v1.md §3 regla 3: la app
            // no revalida esto en C#, ManejadorExcepciones ya traduce el RAISE EXCEPTION a 409).
            migrationBuilder.Sql(@"
create or replace function fn_verificar_email_unico_usuarios()
returns trigger language plpgsql as $$
begin
  if exists (select 1 from clientes_usuarios where email = new.email) then
    raise exception 'El email % ya está en uso por un login de cliente.', new.email;
  end if;
  return new;
end $$;");

            migrationBuilder.Sql(
                "create trigger trg_verificar_email_unico_usuarios " +
                "before insert or update of email on usuarios " +
                "for each row execute function fn_verificar_email_unico_usuarios();");

            migrationBuilder.Sql(@"
create or replace function fn_verificar_email_unico_clientes_usuarios()
returns trigger language plpgsql as $$
begin
  if exists (select 1 from usuarios where email = new.email) then
    raise exception 'El email % ya está en uso por un usuario interno.', new.email;
  end if;
  return new;
end $$;");

            migrationBuilder.Sql(
                "create trigger trg_verificar_email_unico_clientes_usuarios " +
                "before insert or update of email on clientes_usuarios " +
                "for each row execute function fn_verificar_email_unico_clientes_usuarios();");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Nota: revierte el shape, no restaura los datos movidos por Up() a clientes_usuarios
            // (ni los refresh tokens borrados) — igual que el resto de las migraciones de este
            // proyecto, Down() no es un backup.
            migrationBuilder.Sql("drop trigger if exists trg_verificar_email_unico_clientes_usuarios on clientes_usuarios;");
            migrationBuilder.Sql("drop function if exists fn_verificar_email_unico_clientes_usuarios();");
            migrationBuilder.Sql("drop trigger if exists trg_verificar_email_unico_usuarios on usuarios;");
            migrationBuilder.Sql("drop function if exists fn_verificar_email_unico_usuarios();");

            migrationBuilder.DropForeignKey(
                name: "FK_refresh_tokens_clientes_usuarios_cliente_usuario_id",
                table: "refresh_tokens");

            migrationBuilder.DropTable(
                name: "clientes_usuarios");

            migrationBuilder.DropCheckConstraint(
                name: "ck_usuarios_rol",
                table: "usuarios");

            migrationBuilder.DropIndex(
                name: "IX_refresh_tokens_cliente_usuario_id",
                table: "refresh_tokens");

            migrationBuilder.DropCheckConstraint(
                name: "ck_refresh_tokens_actor_unico",
                table: "refresh_tokens");

            migrationBuilder.DropColumn(
                name: "cliente_usuario_id",
                table: "refresh_tokens");

            migrationBuilder.AddColumn<int>(
                name: "cliente_id",
                table: "usuarios",
                type: "integer",
                nullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "usuario_id",
                table: "refresh_tokens",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_usuarios_cliente_id",
                table: "usuarios",
                column: "cliente_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_usuarios_cliente_coherente",
                table: "usuarios",
                sql: "rol <> 'cliente' or cliente_id is not null");

            migrationBuilder.AddCheckConstraint(
                name: "ck_usuarios_rol",
                table: "usuarios",
                sql: "rol in ('administracion','operacion','repartidor','cliente')");

            migrationBuilder.AddForeignKey(
                name: "FK_usuarios_clientes_cliente_id",
                table: "usuarios",
                column: "cliente_id",
                principalTable: "clientes",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
