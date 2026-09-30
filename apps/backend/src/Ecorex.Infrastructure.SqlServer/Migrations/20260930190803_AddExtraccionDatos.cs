using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddExtraccionDatos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "extraccion_definiciones",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    codigo = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    nombre = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    descripcion = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    estado = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ciclo = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    url = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    destino = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    legacy_reg = table.Column<int>(type: "int", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_extraccion_definiciones", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "extraccion_apis",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    definicion_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    legacy_reg = table.Column<int>(type: "int", nullable: true),
                    nombre = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    xml_config = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_extraccion_apis", x => x.id);
                    table.ForeignKey(
                        name: "fk_extraccion_apis_extraccion_definiciones_definicion_id",
                        column: x => x.definicion_id,
                        principalTable: "extraccion_definiciones",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "extraccion_clientes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    definicion_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    legacy_reg = table.Column<int>(type: "int", nullable: true),
                    nombre = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    correo = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    slack = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    token = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    estado = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    referencia = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_extraccion_clientes", x => x.id);
                    table.ForeignKey(
                        name: "fk_extraccion_clientes_extraccion_definiciones_definicion_id",
                        column: x => x.definicion_id,
                        principalTable: "extraccion_definiciones",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "extraccion_pasos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    definicion_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    legacy_reg = table.Column<int>(type: "int", nullable: true),
                    nombre_paso = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    url_paso = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    orden = table.Column<int>(type: "int", nullable: false),
                    tiempo = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    inicia = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    termina = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    relevo = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    sql_explora = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    flag_repetir = table.Column<bool>(type: "bit", nullable: false),
                    flag_no_navegar = table.Column<bool>(type: "bit", nullable: false),
                    flag_url_token = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_extraccion_pasos", x => x.id);
                    table.ForeignKey(
                        name: "fk_extraccion_pasos_extraccion_definiciones_definicion_id",
                        column: x => x.definicion_id,
                        principalTable: "extraccion_definiciones",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "extraccion_seguimientos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    definicion_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    legacy_reg = table.Column<int>(type: "int", nullable: true),
                    seguimiento = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    estado = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_extraccion_seguimientos", x => x.id);
                    table.ForeignKey(
                        name: "fk_extraccion_seguimientos_extraccion_definiciones_definicion_id",
                        column: x => x.definicion_id,
                        principalTable: "extraccion_definiciones",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "extraccion_api_variables",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    api_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    legacy_reg = table.Column<int>(type: "int", nullable: true),
                    nombre = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    valor = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_extraccion_api_variables", x => x.id);
                    table.ForeignKey(
                        name: "fk_extraccion_api_variables_extraccion_apis_api_id",
                        column: x => x.api_id,
                        principalTable: "extraccion_apis",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "extraccion_cliente_variables",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    cliente_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    legacy_reg = table.Column<int>(type: "int", nullable: true),
                    nombre = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    valor_legacy_cifrado = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_extraccion_cliente_variables", x => x.id);
                    table.ForeignKey(
                        name: "fk_extraccion_cliente_variables_extraccion_clientes_cliente_id",
                        column: x => x.cliente_id,
                        principalTable: "extraccion_clientes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "extraccion_acciones",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    paso_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    legacy_reg = table.Column<int>(type: "int", nullable: true),
                    legacy_ped_reg = table.Column<int>(type: "int", nullable: true),
                    script = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    tipo = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    contenedor_codigo = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    espera = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    orden = table.Column<int>(type: "int", nullable: false),
                    condicion = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    valor = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    pagina_desde = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    pagina_hasta = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    variable = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    operacion = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    api_nombre = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    sql_explora = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_extraccion_acciones", x => x.id);
                    table.ForeignKey(
                        name: "fk_extraccion_acciones_extraccion_pasos_paso_id",
                        column: x => x.paso_id,
                        principalTable: "extraccion_pasos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "extraccion_advertencias",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    paso_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    legacy_reg = table.Column<int>(type: "int", nullable: true),
                    etiqueta = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    accion = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_extraccion_advertencias", x => x.id);
                    table.ForeignKey(
                        name: "fk_extraccion_advertencias_extraccion_pasos_paso_id",
                        column: x => x.paso_id,
                        principalTable: "extraccion_pasos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_extraccion_acciones_paso_id",
                table: "extraccion_acciones",
                column: "paso_id");

            migrationBuilder.CreateIndex(
                name: "ix_extraccion_acciones_tenant_id_paso_id_orden",
                table: "extraccion_acciones",
                columns: new[] { "tenant_id", "paso_id", "orden" });

            migrationBuilder.CreateIndex(
                name: "ix_extraccion_advertencias_paso_id",
                table: "extraccion_advertencias",
                column: "paso_id");

            migrationBuilder.CreateIndex(
                name: "ix_extraccion_advertencias_tenant_id_paso_id",
                table: "extraccion_advertencias",
                columns: new[] { "tenant_id", "paso_id" });

            migrationBuilder.CreateIndex(
                name: "ix_extraccion_api_variables_api_id",
                table: "extraccion_api_variables",
                column: "api_id");

            migrationBuilder.CreateIndex(
                name: "ix_extraccion_api_variables_tenant_id_api_id",
                table: "extraccion_api_variables",
                columns: new[] { "tenant_id", "api_id" });

            migrationBuilder.CreateIndex(
                name: "ix_extraccion_apis_definicion_id",
                table: "extraccion_apis",
                column: "definicion_id");

            migrationBuilder.CreateIndex(
                name: "ix_extraccion_apis_tenant_id_definicion_id",
                table: "extraccion_apis",
                columns: new[] { "tenant_id", "definicion_id" });

            migrationBuilder.CreateIndex(
                name: "ix_extraccion_cliente_variables_cliente_id",
                table: "extraccion_cliente_variables",
                column: "cliente_id");

            migrationBuilder.CreateIndex(
                name: "ix_extraccion_cliente_variables_tenant_id_cliente_id",
                table: "extraccion_cliente_variables",
                columns: new[] { "tenant_id", "cliente_id" });

            migrationBuilder.CreateIndex(
                name: "ix_extraccion_clientes_definicion_id",
                table: "extraccion_clientes",
                column: "definicion_id");

            migrationBuilder.CreateIndex(
                name: "ix_extraccion_clientes_tenant_id_definicion_id",
                table: "extraccion_clientes",
                columns: new[] { "tenant_id", "definicion_id" });

            migrationBuilder.CreateIndex(
                name: "ix_extraccion_definiciones_tenant_id_codigo",
                table: "extraccion_definiciones",
                columns: new[] { "tenant_id", "codigo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_extraccion_pasos_definicion_id",
                table: "extraccion_pasos",
                column: "definicion_id");

            migrationBuilder.CreateIndex(
                name: "ix_extraccion_pasos_tenant_id_definicion_id_orden",
                table: "extraccion_pasos",
                columns: new[] { "tenant_id", "definicion_id", "orden" });

            migrationBuilder.CreateIndex(
                name: "ix_extraccion_seguimientos_definicion_id",
                table: "extraccion_seguimientos",
                column: "definicion_id");

            migrationBuilder.CreateIndex(
                name: "ix_extraccion_seguimientos_tenant_id_definicion_id",
                table: "extraccion_seguimientos",
                columns: new[] { "tenant_id", "definicion_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "extraccion_acciones");

            migrationBuilder.DropTable(
                name: "extraccion_advertencias");

            migrationBuilder.DropTable(
                name: "extraccion_api_variables");

            migrationBuilder.DropTable(
                name: "extraccion_cliente_variables");

            migrationBuilder.DropTable(
                name: "extraccion_seguimientos");

            migrationBuilder.DropTable(
                name: "extraccion_pasos");

            migrationBuilder.DropTable(
                name: "extraccion_apis");

            migrationBuilder.DropTable(
                name: "extraccion_clientes");

            migrationBuilder.DropTable(
                name: "extraccion_definiciones");
        }
    }
}
