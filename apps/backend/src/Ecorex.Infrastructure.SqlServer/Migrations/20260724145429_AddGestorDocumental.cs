using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class AddGestorDocumental : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "documento_categorias",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    nombre = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    descripcion = table.Column<string>(type: "nvarchar(600)", maxLength: 600, nullable: true),
                    icono = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    color = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    es_base = table.Column<bool>(type: "bit", nullable: false),
                    activa = table.Column<bool>(type: "bit", nullable: false),
                    orden = table.Column<int>(type: "int", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_documento_categorias", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "documento_etiqueta_catalogos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    nombre = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    color = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    es_base = table.Column<bool>(type: "bit", nullable: false),
                    activa = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_documento_etiqueta_catalogos", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "expedientes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    codigo = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    nombre = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    serie = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    subserie = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    subserie_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    activo = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_expedientes", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "series_documentales",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    nombre = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    orden = table.Column<int>(type: "int", nullable: false),
                    activa = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_series_documentales", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "documento_carpetas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    categoria_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    padre_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    nombre = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    descripcion = table.Column<string>(type: "nvarchar(600)", maxLength: 600, nullable: true),
                    activa = table.Column<bool>(type: "bit", nullable: false),
                    orden = table.Column<int>(type: "int", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_documento_carpetas", x => x.id);
                    table.ForeignKey(
                        name: "fk_documento_carpetas_documento_carpetas_padre_id",
                        column: x => x.padre_id,
                        principalTable: "documento_carpetas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_documento_carpetas_documento_categorias_categoria_id",
                        column: x => x.categoria_id,
                        principalTable: "documento_categorias",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "expediente_campos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    expediente_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    clave = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    label = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    orden = table.Column<int>(type: "int", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_expediente_campos", x => x.id);
                    table.ForeignKey(
                        name: "fk_expediente_campos_expedientes_expediente_id",
                        column: x => x.expediente_id,
                        principalTable: "expedientes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "expediente_tipologias",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    expediente_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    nombre = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    obligatoria = table.Column<bool>(type: "bit", nullable: false),
                    orden = table.Column<int>(type: "int", nullable: false),
                    archivo_url = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    archivo_nombre = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    archivo_mime = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    archivo_tamano = table.Column<long>(type: "bigint", nullable: false),
                    archivo_hash_sha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    meta_json = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_expediente_tipologias", x => x.id);
                    table.ForeignKey(
                        name: "fk_expediente_tipologias_expedientes_expediente_id",
                        column: x => x.expediente_id,
                        principalTable: "expedientes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "subseries_documentales",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    serie_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    nombre = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    orden = table.Column<int>(type: "int", nullable: false),
                    activa = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_subseries_documentales", x => x.id);
                    table.ForeignKey(
                        name: "fk_subseries_documentales_series_documentales_serie_id",
                        column: x => x.serie_id,
                        principalTable: "series_documentales",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "subserie_campos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    subserie_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    clave = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    label = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    orden = table.Column<int>(type: "int", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_subserie_campos", x => x.id);
                    table.ForeignKey(
                        name: "fk_subserie_campos_subseries_documentales_subserie_id",
                        column: x => x.subserie_id,
                        principalTable: "subseries_documentales",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "subserie_tipologias",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    subserie_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    nombre = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    obligatoria = table.Column<bool>(type: "bit", nullable: false),
                    orden = table.Column<int>(type: "int", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_subserie_tipologias", x => x.id);
                    table.ForeignKey(
                        name: "fk_subserie_tipologias_subseries_documentales_subserie_id",
                        column: x => x.subserie_id,
                        principalTable: "subseries_documentales",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "documento_auditorias",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    documento_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tipo_evento = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    detalle_json = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    usuario_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ocurrido_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_documento_auditorias", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "documento_consumos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    documento_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    version_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tipo_evento = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    dispositivo = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    usuario_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ocurrido_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_documento_consumos", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "documento_destacados_personales",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    documento_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    usuario_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_documento_destacados_personales", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "documento_etiquetas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    documento_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    etiqueta_catalogo_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_documento_etiquetas", x => x.id);
                    table.ForeignKey(
                        name: "fk_documento_etiquetas_documento_etiqueta_catalogos_etiqueta_catalogo_id",
                        column: x => x.etiqueta_catalogo_id,
                        principalTable: "documento_etiqueta_catalogos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "documento_versiones",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    documento_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    numero = table.Column<int>(type: "int", nullable: false),
                    nombre_archivo = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    tipo_mime = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    tamano_bytes = table.Column<long>(type: "bigint", nullable: false),
                    url_storage = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    hash_sha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    notas_cambio = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    subido_por_usuario_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_documento_versiones", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "documentos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    categoria_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    carpeta_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    titulo = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    descripcion = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    nombre_archivo_original = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    origen = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    origen_entidad_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    estado = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    visibilidad = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    version_actual_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    numero_versiones = table.Column<int>(type: "int", nullable: false),
                    destacado = table.Column<bool>(type: "bit", nullable: false),
                    activo = table.Column<bool>(type: "bit", nullable: false),
                    subido_por_usuario_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_documentos", x => x.id);
                    table.ForeignKey(
                        name: "fk_documentos_documento_carpetas_carpeta_id",
                        column: x => x.carpeta_id,
                        principalTable: "documento_carpetas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_documentos_documento_categorias_categoria_id",
                        column: x => x.categoria_id,
                        principalTable: "documento_categorias",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_documentos_documento_versiones_version_actual_id",
                        column: x => x.version_actual_id,
                        principalTable: "documento_versiones",
                        principalColumn: "id");
                });

            migrationBuilder.CreateIndex(
                name: "ix_documento_auditorias_documento_id",
                table: "documento_auditorias",
                column: "documento_id");

            migrationBuilder.CreateIndex(
                name: "ix_documento_auditorias_tenant_id_documento_id_ocurrido_at",
                table: "documento_auditorias",
                columns: new[] { "tenant_id", "documento_id", "ocurrido_at" });

            migrationBuilder.CreateIndex(
                name: "ix_documento_carpetas_categoria_id",
                table: "documento_carpetas",
                column: "categoria_id");

            migrationBuilder.CreateIndex(
                name: "ix_documento_carpetas_padre_id",
                table: "documento_carpetas",
                column: "padre_id");

            migrationBuilder.CreateIndex(
                name: "ix_documento_carpetas_tenant_id_categoria_id_padre_id",
                table: "documento_carpetas",
                columns: new[] { "tenant_id", "categoria_id", "padre_id" });

            migrationBuilder.CreateIndex(
                name: "ix_documento_categorias_tenant_id_activa_orden",
                table: "documento_categorias",
                columns: new[] { "tenant_id", "activa", "orden" });

            migrationBuilder.CreateIndex(
                name: "ix_documento_categorias_tenant_id_nombre",
                table: "documento_categorias",
                columns: new[] { "tenant_id", "nombre" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_documento_consumos_documento_id",
                table: "documento_consumos",
                column: "documento_id");

            migrationBuilder.CreateIndex(
                name: "ix_documento_consumos_tenant_id_documento_id_ocurrido_at",
                table: "documento_consumos",
                columns: new[] { "tenant_id", "documento_id", "ocurrido_at" });

            migrationBuilder.CreateIndex(
                name: "ix_documento_consumos_version_id",
                table: "documento_consumos",
                column: "version_id");

            migrationBuilder.CreateIndex(
                name: "ix_documento_destacados_personales_documento_id_usuario_id",
                table: "documento_destacados_personales",
                columns: new[] { "documento_id", "usuario_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_documento_destacados_personales_tenant_id_usuario_id",
                table: "documento_destacados_personales",
                columns: new[] { "tenant_id", "usuario_id" });

            migrationBuilder.CreateIndex(
                name: "ix_documento_etiqueta_catalogos_tenant_id_nombre",
                table: "documento_etiqueta_catalogos",
                columns: new[] { "tenant_id", "nombre" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_documento_etiquetas_documento_id_etiqueta_catalogo_id",
                table: "documento_etiquetas",
                columns: new[] { "documento_id", "etiqueta_catalogo_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_documento_etiquetas_etiqueta_catalogo_id",
                table: "documento_etiquetas",
                column: "etiqueta_catalogo_id");

            migrationBuilder.CreateIndex(
                name: "ix_documento_etiquetas_tenant_id_etiqueta_catalogo_id",
                table: "documento_etiquetas",
                columns: new[] { "tenant_id", "etiqueta_catalogo_id" });

            migrationBuilder.CreateIndex(
                name: "ix_documento_versiones_documento_id_numero",
                table: "documento_versiones",
                columns: new[] { "documento_id", "numero" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_documentos_carpeta_id",
                table: "documentos",
                column: "carpeta_id");

            migrationBuilder.CreateIndex(
                name: "ix_documentos_categoria_id",
                table: "documentos",
                column: "categoria_id");

            migrationBuilder.CreateIndex(
                name: "ix_documentos_tenant_id_activo_categoria_id",
                table: "documentos",
                columns: new[] { "tenant_id", "activo", "categoria_id" });

            migrationBuilder.CreateIndex(
                name: "ix_documentos_tenant_id_carpeta_id",
                table: "documentos",
                columns: new[] { "tenant_id", "carpeta_id" });

            migrationBuilder.CreateIndex(
                name: "ix_documentos_tenant_id_estado",
                table: "documentos",
                columns: new[] { "tenant_id", "estado" });

            migrationBuilder.CreateIndex(
                name: "ix_documentos_tenant_id_origen_origen_entidad_id",
                table: "documentos",
                columns: new[] { "tenant_id", "origen", "origen_entidad_id" });

            migrationBuilder.CreateIndex(
                name: "ix_documentos_version_actual_id",
                table: "documentos",
                column: "version_actual_id");

            migrationBuilder.CreateIndex(
                name: "ix_expediente_campos_expediente_id_clave",
                table: "expediente_campos",
                columns: new[] { "expediente_id", "clave" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_expediente_tipologias_expediente_id_orden",
                table: "expediente_tipologias",
                columns: new[] { "expediente_id", "orden" });

            migrationBuilder.CreateIndex(
                name: "ix_expedientes_tenant_id_activo",
                table: "expedientes",
                columns: new[] { "tenant_id", "activo" });

            migrationBuilder.CreateIndex(
                name: "ix_expedientes_tenant_id_codigo",
                table: "expedientes",
                columns: new[] { "tenant_id", "codigo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_series_documentales_tenant_id_nombre",
                table: "series_documentales",
                columns: new[] { "tenant_id", "nombre" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_subserie_campos_subserie_id_clave",
                table: "subserie_campos",
                columns: new[] { "subserie_id", "clave" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_subserie_tipologias_subserie_id_orden",
                table: "subserie_tipologias",
                columns: new[] { "subserie_id", "orden" });

            migrationBuilder.CreateIndex(
                name: "ix_subseries_documentales_serie_id_nombre",
                table: "subseries_documentales",
                columns: new[] { "serie_id", "nombre" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_documento_auditorias_documentos_documento_id",
                table: "documento_auditorias",
                column: "documento_id",
                principalTable: "documentos",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_documento_consumos_documento_versiones_version_id",
                table: "documento_consumos",
                column: "version_id",
                principalTable: "documento_versiones",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_documento_consumos_documentos_documento_id",
                table: "documento_consumos",
                column: "documento_id",
                principalTable: "documentos",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_documento_destacados_personales_documentos_documento_id",
                table: "documento_destacados_personales",
                column: "documento_id",
                principalTable: "documentos",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_documento_etiquetas_documentos_documento_id",
                table: "documento_etiquetas",
                column: "documento_id",
                principalTable: "documentos",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_documento_versiones_documentos_documento_id",
                table: "documento_versiones",
                column: "documento_id",
                principalTable: "documentos",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_documento_versiones_documentos_documento_id",
                table: "documento_versiones");

            migrationBuilder.DropTable(
                name: "documento_auditorias");

            migrationBuilder.DropTable(
                name: "documento_consumos");

            migrationBuilder.DropTable(
                name: "documento_destacados_personales");

            migrationBuilder.DropTable(
                name: "documento_etiquetas");

            migrationBuilder.DropTable(
                name: "expediente_campos");

            migrationBuilder.DropTable(
                name: "expediente_tipologias");

            migrationBuilder.DropTable(
                name: "subserie_campos");

            migrationBuilder.DropTable(
                name: "subserie_tipologias");

            migrationBuilder.DropTable(
                name: "documento_etiqueta_catalogos");

            migrationBuilder.DropTable(
                name: "expedientes");

            migrationBuilder.DropTable(
                name: "subseries_documentales");

            migrationBuilder.DropTable(
                name: "series_documentales");

            migrationBuilder.DropTable(
                name: "documentos");

            migrationBuilder.DropTable(
                name: "documento_carpetas");

            migrationBuilder.DropTable(
                name: "documento_versiones");

            migrationBuilder.DropTable(
                name: "documento_categorias");
        }
    }
}
