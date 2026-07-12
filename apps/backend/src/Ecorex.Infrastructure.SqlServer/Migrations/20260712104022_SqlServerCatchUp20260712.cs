using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class SqlServerCatchUp20260712 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "activity_type_id",
                table: "task_items",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AddColumn<Guid>(
                name: "entidad_id",
                table: "task_items",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "milestone_id",
                table: "task_items",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "subcategoria_id",
                table: "task_items",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_responsible",
                table: "org_unit_members",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "is_process_group",
                table: "menu_nodes",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "datos_tienda_json",
                table: "items",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "es_principal",
                table: "item_images",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "texto",
                table: "item_images",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "actividad_categorias",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    codigo = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    nombre = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    descripcion = table.Column<string>(type: "nvarchar(600)", maxLength: 600, nullable: true),
                    sort_order = table.Column<int>(type: "int", nullable: false),
                    is_archived = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_actividad_categorias", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "bolsa_columnas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    nombre = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    color = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    sort_order = table.Column<int>(type: "int", nullable: false),
                    es_cliente = table.Column<bool>(type: "bit", nullable: false),
                    is_archived = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bolsa_columnas", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "data_clients",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    description = table.Column<string>(type: "nvarchar(600)", maxLength: 600, nullable: true),
                    client_id = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    client_secret_encrypted = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_data_clients", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "data_models",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_data_models", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "entidad_field_definitions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    field_key = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    label = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    field_type = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    options = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    column = table.Column<int>(type: "int", nullable: false),
                    sort_order = table.Column<int>(type: "int", nullable: false),
                    description = table.Column<string>(type: "nvarchar(600)", maxLength: 600, nullable: true),
                    is_required = table.Column<bool>(type: "bit", nullable: false),
                    is_system = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_entidad_field_definitions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "entidades",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    codigo = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    nombre = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    nombre_comercial = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    sigla = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    tipo_entidad = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    tax_id = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    tax_id_dv = table.Column<string>(type: "nvarchar(5)", maxLength: 5, nullable: true),
                    representante_legal = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    naturaleza_juridica = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    pais = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    departamento = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    ciudad = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    direccion = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    telefono = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    email = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    web = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    zona_horaria = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    idioma = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    observaciones = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    logo_base64 = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    is_principal = table.Column<bool>(type: "bit", nullable: false),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    is_archived = table.Column<bool>(type: "bit", nullable: false),
                    sort_order = table.Column<int>(type: "int", nullable: false),
                    field_values_json = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_entidades", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "item_field_definitions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    item_type_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    field_key = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    label = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    field_type = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    options = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    column = table.Column<int>(type: "int", nullable: false),
                    sort_order = table.Column<int>(type: "int", nullable: false),
                    description = table.Column<string>(type: "nvarchar(600)", maxLength: 600, nullable: true),
                    is_required = table.Column<bool>(type: "bit", nullable: false),
                    is_system = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_item_field_definitions", x => x.id);
                    table.ForeignKey(
                        name: "fk_item_field_definitions_item_types_item_type_id",
                        column: x => x.item_type_id,
                        principalTable: "item_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "notifications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    recipient_tenant_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    kind = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    body = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    link_route = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    related_task_item_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    actor_name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    is_read = table.Column<bool>(type: "bit", nullable: false),
                    read_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notifications", x => x.id);
                    table.ForeignKey(
                        name: "fk_notifications_tenant_users_recipient_tenant_user_id",
                        column: x => x.recipient_tenant_user_id,
                        principalTable: "tenant_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "project_milestones",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    project_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    due_date = table.Column<DateOnly>(type: "date", nullable: true),
                    sort_order = table.Column<int>(type: "int", nullable: false),
                    is_completed = table.Column<bool>(type: "bit", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_project_milestones", x => x.id);
                    table.ForeignKey(
                        name: "fk_project_milestones_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tercero_field_definitions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ficha_key = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    field_key = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    label = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    field_type = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    options = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    column = table.Column<int>(type: "int", nullable: false),
                    sort_order = table.Column<int>(type: "int", nullable: false),
                    description = table.Column<string>(type: "nvarchar(600)", maxLength: 600, nullable: true),
                    allow_multiple = table.Column<bool>(type: "bit", nullable: false),
                    is_system = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tercero_field_definitions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "tercero_filtros",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    nombre = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    descripcion = table.Column<string>(type: "nvarchar(600)", maxLength: 600, nullable: true),
                    fuente = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    criterios_json = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    conteo_anterior = table.Column<int>(type: "int", nullable: false),
                    fecha_snapshot = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    sort_order = table.Column<int>(type: "int", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tercero_filtros", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "actividad_subcategorias",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    categoria_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    codigo = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    nombre = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    chequeo = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    descripcion = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    sort_order = table.Column<int>(type: "int", nullable: false),
                    is_archived = table.Column<bool>(type: "bit", nullable: false),
                    requiere_cliente = table.Column<bool>(type: "bit", nullable: false),
                    inicia_modulo = table.Column<bool>(type: "bit", nullable: false),
                    cierre_manual = table.Column<bool>(type: "bit", nullable: false),
                    titulo_auto = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    detalle_auto = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    sedes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    workflow_definition_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    form_definition_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    task_board_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    task_board_column_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_actividad_subcategorias", x => x.id);
                    table.ForeignKey(
                        name: "fk_actividad_subcategorias_actividad_categorias_categoria_id",
                        column: x => x.categoria_id,
                        principalTable: "actividad_categorias",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_actividad_subcategorias_form_definitions_form_definition_id",
                        column: x => x.form_definition_id,
                        principalTable: "form_definitions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_actividad_subcategorias_task_board_columns_task_board_column_id",
                        column: x => x.task_board_column_id,
                        principalTable: "task_board_columns",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_actividad_subcategorias_task_boards_task_board_id",
                        column: x => x.task_board_id,
                        principalTable: "task_boards",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_actividad_subcategorias_workflow_definitions_workflow_definition_id",
                        column: x => x.workflow_definition_id,
                        principalTable: "workflow_definitions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "terceros",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    nombre = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    tipo = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    perfiles = table.Column<int>(type: "int", nullable: false),
                    estado = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    vendedor = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    ciudad = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    id_tipo = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    id_valor = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    sector = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    cargo = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    email = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    telefono = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    empresa_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    fichas_json = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    bolsa_columna_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_terceros", x => x.id);
                    table.ForeignKey(
                        name: "fk_terceros_bolsa_columnas_bolsa_columna_id",
                        column: x => x.bolsa_columna_id,
                        principalTable: "bolsa_columnas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_terceros_terceros_empresa_id",
                        column: x => x.empresa_id,
                        principalTable: "terceros",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "data_containers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    model_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    canvas_x = table.Column<double>(type: "float", nullable: false),
                    canvas_y = table.Column<double>(type: "float", nullable: false),
                    source_kind = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    parent_container_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    parent_field_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_data_containers", x => x.id);
                    table.ForeignKey(
                        name: "fk_data_containers_data_containers_parent_container_id",
                        column: x => x.parent_container_id,
                        principalTable: "data_containers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_data_containers_data_models_model_id",
                        column: x => x.model_id,
                        principalTable: "data_models",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "data_destinations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    model_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    kind = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    db_engine = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    host = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    port = table.Column<int>(type: "int", nullable: true),
                    database_name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    username = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    credentials_encrypted = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_data_destinations", x => x.id);
                    table.ForeignKey(
                        name: "fk_data_destinations_data_models_model_id",
                        column: x => x.model_id,
                        principalTable: "data_models",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "actividad_subcategoria_cargos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    subcategoria_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    org_unit_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_actividad_subcategoria_cargos", x => x.id);
                    table.ForeignKey(
                        name: "fk_actividad_subcategoria_cargos_actividad_subcategorias_subcategoria_id",
                        column: x => x.subcategoria_id,
                        principalTable: "actividad_subcategorias",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_actividad_subcategoria_cargos_org_units_org_unit_id",
                        column: x => x.org_unit_id,
                        principalTable: "org_units",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "actividad_subcategoria_notificaciones",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    subcategoria_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tenant_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_actividad_subcategoria_notificaciones", x => x.id);
                    table.ForeignKey(
                        name: "fk_actividad_subcategoria_notificaciones_actividad_subcategorias_subcategoria_id",
                        column: x => x.subcategoria_id,
                        principalTable: "actividad_subcategorias",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_actividad_subcategoria_notificaciones_tenant_users_tenant_user_id",
                        column: x => x.tenant_user_id,
                        principalTable: "tenant_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "actividad_subcategoria_terceros",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    subcategoria_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tercero_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_actividad_subcategoria_terceros", x => x.id);
                    table.ForeignKey(
                        name: "fk_actividad_subcategoria_terceros_actividad_subcategorias_subcategoria_id",
                        column: x => x.subcategoria_id,
                        principalTable: "actividad_subcategorias",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_actividad_subcategoria_terceros_terceros_tercero_id",
                        column: x => x.tercero_id,
                        principalTable: "terceros",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "oportunidades",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tercero_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    nombre = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    etapa = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    valor = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    responsable = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    probabilidad = table.Column<int>(type: "int", nullable: false),
                    fecha_cierre = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    fuente = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    descripcion = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    sort_order = table.Column<int>(type: "int", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_oportunidades", x => x.id);
                    table.ForeignKey(
                        name: "fk_oportunidades_terceros_tercero_id",
                        column: x => x.tercero_id,
                        principalTable: "terceros",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "prospectos_scrapeados",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    fuente = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    nombre_completo = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    cargo = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    empresa = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ciudad = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    metrica = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    badge = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    telefono = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    correo = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    data_json = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    tercero_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    fecha_captura = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_prospectos_scrapeados", x => x.id);
                    table.ForeignKey(
                        name: "fk_prospectos_scrapeados_terceros_tercero_id",
                        column: x => x.tercero_id,
                        principalTable: "terceros",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "tercero_contactos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tercero_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    nombre = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    cargo = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    email = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    telefono = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tercero_contactos", x => x.id);
                    table.ForeignKey(
                        name: "fk_tercero_contactos_terceros_tercero_id",
                        column: x => x.tercero_id,
                        principalTable: "terceros",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tercero_notas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tercero_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    texto = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    accion = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    categoria = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    subcategoria = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    autor = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tercero_notas", x => x.id);
                    table.ForeignKey(
                        name: "fk_tercero_notas_terceros_tercero_id",
                        column: x => x.tercero_id,
                        principalTable: "terceros",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "data_connectors",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    model_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    container_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    kind = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    endpoint_url = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    http_method = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    auth_kind = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    db_engine = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    host = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    port = table.Column<int>(type: "int", nullable: true),
                    database_name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    username = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    credentials_encrypted = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    mapping_json = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_data_connectors", x => x.id);
                    table.ForeignKey(
                        name: "fk_data_connectors_data_containers_container_id",
                        column: x => x.container_id,
                        principalTable: "data_containers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_data_connectors_data_models_model_id",
                        column: x => x.model_id,
                        principalTable: "data_models",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "data_container_columns",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    container_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    description = table.Column<string>(type: "nvarchar(600)", maxLength: 600, nullable: true),
                    type = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    sort_order = table.Column<int>(type: "int", nullable: false),
                    is_required = table.Column<bool>(type: "bit", nullable: false),
                    child_container_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    referenced_container_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_data_container_columns", x => x.id);
                    table.ForeignKey(
                        name: "fk_data_container_columns_data_containers_child_container_id",
                        column: x => x.child_container_id,
                        principalTable: "data_containers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_data_container_columns_data_containers_container_id",
                        column: x => x.container_id,
                        principalTable: "data_containers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_data_container_columns_data_containers_referenced_container_id",
                        column: x => x.referenced_container_id,
                        principalTable: "data_containers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "data_container_rows",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    container_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    parent_row_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    parent_field_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_data_container_rows", x => x.id);
                    table.ForeignKey(
                        name: "fk_data_container_rows_data_container_rows_parent_row_id",
                        column: x => x.parent_row_id,
                        principalTable: "data_container_rows",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_data_container_rows_data_containers_container_id",
                        column: x => x.container_id,
                        principalTable: "data_containers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "citas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    tercero_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    oportunidad_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    titulo = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    tipo = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    inicio = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    duracion_minutos = table.Column<int>(type: "int", nullable: false),
                    nota = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    completada = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_citas", x => x.id);
                    table.ForeignKey(
                        name: "fk_citas_oportunidades_oportunidad_id",
                        column: x => x.oportunidad_id,
                        principalTable: "oportunidades",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_citas_terceros_tercero_id",
                        column: x => x.tercero_id,
                        principalTable: "terceros",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "import_processes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    model_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    container_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    connector_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    client_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    schedule_kind = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    interval_minutes = table.Column<int>(type: "int", nullable: true),
                    cron_expression = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    last_run_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_import_processes", x => x.id);
                    table.ForeignKey(
                        name: "fk_import_processes_data_clients_client_id",
                        column: x => x.client_id,
                        principalTable: "data_clients",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_import_processes_data_connectors_connector_id",
                        column: x => x.connector_id,
                        principalTable: "data_connectors",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_import_processes_data_containers_container_id",
                        column: x => x.container_id,
                        principalTable: "data_containers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_import_processes_data_models_model_id",
                        column: x => x.model_id,
                        principalTable: "data_models",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "data_container_cells",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    row_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    column_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    value = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_data_container_cells", x => x.id);
                    table.ForeignKey(
                        name: "fk_data_container_cells_data_container_columns_column_id",
                        column: x => x.column_id,
                        principalTable: "data_container_columns",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_data_container_cells_data_container_rows_row_id",
                        column: x => x.row_id,
                        principalTable: "data_container_rows",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "data_container_links",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    column_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    row_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    target_row_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_data_container_links", x => x.id);
                    table.ForeignKey(
                        name: "fk_data_container_links_data_container_columns_column_id",
                        column: x => x.column_id,
                        principalTable: "data_container_columns",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_data_container_links_data_container_rows_row_id",
                        column: x => x.row_id,
                        principalTable: "data_container_rows",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_data_container_links_data_container_rows_target_row_id",
                        column: x => x.target_row_id,
                        principalTable: "data_container_rows",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_task_items_entidad_id",
                table: "task_items",
                column: "entidad_id");

            migrationBuilder.CreateIndex(
                name: "ix_task_items_milestone_id",
                table: "task_items",
                column: "milestone_id");

            migrationBuilder.CreateIndex(
                name: "ix_task_items_subcategoria_id",
                table: "task_items",
                column: "subcategoria_id");

            migrationBuilder.CreateIndex(
                name: "ix_task_items_tenant_id_entidad_id",
                table: "task_items",
                columns: new[] { "tenant_id", "entidad_id" });

            migrationBuilder.CreateIndex(
                name: "ix_task_items_tenant_id_milestone_id",
                table: "task_items",
                columns: new[] { "tenant_id", "milestone_id" });

            migrationBuilder.CreateIndex(
                name: "ix_task_items_tenant_id_subcategoria_id",
                table: "task_items",
                columns: new[] { "tenant_id", "subcategoria_id" });

            migrationBuilder.CreateIndex(
                name: "ix_actividad_categorias_tenant_id_codigo",
                table: "actividad_categorias",
                columns: new[] { "tenant_id", "codigo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_actividad_categorias_tenant_id_is_archived_sort_order",
                table: "actividad_categorias",
                columns: new[] { "tenant_id", "is_archived", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "ix_actividad_subcategoria_cargos_org_unit_id",
                table: "actividad_subcategoria_cargos",
                column: "org_unit_id");

            migrationBuilder.CreateIndex(
                name: "ix_actividad_subcategoria_cargos_subcategoria_id_org_unit_id",
                table: "actividad_subcategoria_cargos",
                columns: new[] { "subcategoria_id", "org_unit_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_actividad_subcategoria_cargos_tenant_id_org_unit_id",
                table: "actividad_subcategoria_cargos",
                columns: new[] { "tenant_id", "org_unit_id" });

            migrationBuilder.CreateIndex(
                name: "ix_actividad_subcategoria_notificaciones_subcategoria_id_tenant_user_id",
                table: "actividad_subcategoria_notificaciones",
                columns: new[] { "subcategoria_id", "tenant_user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_actividad_subcategoria_notificaciones_tenant_id_tenant_user_id",
                table: "actividad_subcategoria_notificaciones",
                columns: new[] { "tenant_id", "tenant_user_id" });

            migrationBuilder.CreateIndex(
                name: "ix_actividad_subcategoria_notificaciones_tenant_user_id",
                table: "actividad_subcategoria_notificaciones",
                column: "tenant_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_actividad_subcategoria_terceros_subcategoria_id_tercero_id",
                table: "actividad_subcategoria_terceros",
                columns: new[] { "subcategoria_id", "tercero_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_actividad_subcategoria_terceros_tenant_id_tercero_id",
                table: "actividad_subcategoria_terceros",
                columns: new[] { "tenant_id", "tercero_id" });

            migrationBuilder.CreateIndex(
                name: "ix_actividad_subcategoria_terceros_tercero_id",
                table: "actividad_subcategoria_terceros",
                column: "tercero_id");

            migrationBuilder.CreateIndex(
                name: "ix_actividad_subcategorias_categoria_id",
                table: "actividad_subcategorias",
                column: "categoria_id");

            migrationBuilder.CreateIndex(
                name: "ix_actividad_subcategorias_form_definition_id",
                table: "actividad_subcategorias",
                column: "form_definition_id");

            migrationBuilder.CreateIndex(
                name: "ix_actividad_subcategorias_task_board_column_id",
                table: "actividad_subcategorias",
                column: "task_board_column_id");

            migrationBuilder.CreateIndex(
                name: "ix_actividad_subcategorias_task_board_id",
                table: "actividad_subcategorias",
                column: "task_board_id");

            migrationBuilder.CreateIndex(
                name: "ix_actividad_subcategorias_tenant_id_categoria_id_sort_order",
                table: "actividad_subcategorias",
                columns: new[] { "tenant_id", "categoria_id", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "ix_actividad_subcategorias_tenant_id_codigo",
                table: "actividad_subcategorias",
                columns: new[] { "tenant_id", "codigo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_actividad_subcategorias_workflow_definition_id",
                table: "actividad_subcategorias",
                column: "workflow_definition_id");

            migrationBuilder.CreateIndex(
                name: "ix_bolsa_columnas_tenant_id_is_archived_sort_order",
                table: "bolsa_columnas",
                columns: new[] { "tenant_id", "is_archived", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "ix_citas_oportunidad_id",
                table: "citas",
                column: "oportunidad_id");

            migrationBuilder.CreateIndex(
                name: "ix_citas_tenant_id_inicio",
                table: "citas",
                columns: new[] { "tenant_id", "inicio" });

            migrationBuilder.CreateIndex(
                name: "ix_citas_tenant_id_tercero_id",
                table: "citas",
                columns: new[] { "tenant_id", "tercero_id" });

            migrationBuilder.CreateIndex(
                name: "ix_citas_tercero_id",
                table: "citas",
                column: "tercero_id");

            migrationBuilder.CreateIndex(
                name: "ix_data_clients_tenant_id_client_id",
                table: "data_clients",
                columns: new[] { "tenant_id", "client_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_data_clients_tenant_id_is_active",
                table: "data_clients",
                columns: new[] { "tenant_id", "is_active" });

            migrationBuilder.CreateIndex(
                name: "ix_data_connectors_container_id",
                table: "data_connectors",
                column: "container_id");

            migrationBuilder.CreateIndex(
                name: "ix_data_connectors_model_id",
                table: "data_connectors",
                column: "model_id");

            migrationBuilder.CreateIndex(
                name: "ix_data_connectors_tenant_id_model_id",
                table: "data_connectors",
                columns: new[] { "tenant_id", "model_id" });

            migrationBuilder.CreateIndex(
                name: "ix_data_container_cells_column_id",
                table: "data_container_cells",
                column: "column_id");

            migrationBuilder.CreateIndex(
                name: "ix_data_container_cells_row_id_column_id",
                table: "data_container_cells",
                columns: new[] { "row_id", "column_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_data_container_columns_child_container_id",
                table: "data_container_columns",
                column: "child_container_id");

            migrationBuilder.CreateIndex(
                name: "ix_data_container_columns_container_id_name",
                table: "data_container_columns",
                columns: new[] { "container_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_data_container_columns_referenced_container_id",
                table: "data_container_columns",
                column: "referenced_container_id");

            migrationBuilder.CreateIndex(
                name: "ix_data_container_columns_tenant_id_container_id_sort_order",
                table: "data_container_columns",
                columns: new[] { "tenant_id", "container_id", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "ix_data_container_links_column_id_row_id_target_row_id",
                table: "data_container_links",
                columns: new[] { "column_id", "row_id", "target_row_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_data_container_links_row_id",
                table: "data_container_links",
                column: "row_id");

            migrationBuilder.CreateIndex(
                name: "ix_data_container_links_target_row_id",
                table: "data_container_links",
                column: "target_row_id");

            migrationBuilder.CreateIndex(
                name: "ix_data_container_links_tenant_id_target_row_id",
                table: "data_container_links",
                columns: new[] { "tenant_id", "target_row_id" });

            migrationBuilder.CreateIndex(
                name: "ix_data_container_rows_container_id",
                table: "data_container_rows",
                column: "container_id");

            migrationBuilder.CreateIndex(
                name: "ix_data_container_rows_parent_row_id_parent_field_id",
                table: "data_container_rows",
                columns: new[] { "parent_row_id", "parent_field_id" });

            migrationBuilder.CreateIndex(
                name: "ix_data_container_rows_tenant_id_container_id_created_at",
                table: "data_container_rows",
                columns: new[] { "tenant_id", "container_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_data_containers_model_id_name",
                table: "data_containers",
                columns: new[] { "model_id", "name" },
                unique: true,
                filter: "[model_id] IS NOT NULL AND [parent_container_id] IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_data_containers_parent_container_id",
                table: "data_containers",
                column: "parent_container_id");

            migrationBuilder.CreateIndex(
                name: "ix_data_containers_tenant_id_model_id",
                table: "data_containers",
                columns: new[] { "tenant_id", "model_id" });

            migrationBuilder.CreateIndex(
                name: "ix_data_containers_tenant_id_parent_container_id",
                table: "data_containers",
                columns: new[] { "tenant_id", "parent_container_id" });

            migrationBuilder.CreateIndex(
                name: "ix_data_destinations_model_id",
                table: "data_destinations",
                column: "model_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_data_models_tenant_id_name",
                table: "data_models",
                columns: new[] { "tenant_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_entidad_field_definitions_tenant_id_field_key",
                table: "entidad_field_definitions",
                columns: new[] { "tenant_id", "field_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_entidad_field_definitions_tenant_id_sort_order",
                table: "entidad_field_definitions",
                columns: new[] { "tenant_id", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "ix_entidades_tenant_id_codigo",
                table: "entidades",
                columns: new[] { "tenant_id", "codigo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_entidades_tenant_id_is_archived",
                table: "entidades",
                columns: new[] { "tenant_id", "is_archived" });

            migrationBuilder.CreateIndex(
                name: "ix_import_processes_client_id",
                table: "import_processes",
                column: "client_id");

            migrationBuilder.CreateIndex(
                name: "ix_import_processes_connector_id",
                table: "import_processes",
                column: "connector_id");

            migrationBuilder.CreateIndex(
                name: "ix_import_processes_container_id",
                table: "import_processes",
                column: "container_id");

            migrationBuilder.CreateIndex(
                name: "ix_import_processes_model_id",
                table: "import_processes",
                column: "model_id");

            migrationBuilder.CreateIndex(
                name: "ix_import_processes_tenant_id_model_id",
                table: "import_processes",
                columns: new[] { "tenant_id", "model_id" });

            migrationBuilder.CreateIndex(
                name: "ix_item_field_definitions_item_type_id",
                table: "item_field_definitions",
                column: "item_type_id");

            migrationBuilder.CreateIndex(
                name: "ix_item_field_definitions_tenant_id_item_type_id_field_key",
                table: "item_field_definitions",
                columns: new[] { "tenant_id", "item_type_id", "field_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_item_field_definitions_tenant_id_item_type_id_sort_order",
                table: "item_field_definitions",
                columns: new[] { "tenant_id", "item_type_id", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "ix_notifications_recipient_tenant_user_id_is_read_created_at",
                table: "notifications",
                columns: new[] { "recipient_tenant_user_id", "is_read", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_oportunidades_tenant_id_etapa_sort_order",
                table: "oportunidades",
                columns: new[] { "tenant_id", "etapa", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "ix_oportunidades_tenant_id_tercero_id",
                table: "oportunidades",
                columns: new[] { "tenant_id", "tercero_id" });

            migrationBuilder.CreateIndex(
                name: "ix_oportunidades_tercero_id",
                table: "oportunidades",
                column: "tercero_id");

            migrationBuilder.CreateIndex(
                name: "ix_project_milestones_project_id_sort_order",
                table: "project_milestones",
                columns: new[] { "project_id", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "ix_prospectos_scrapeados_tenant_id_fuente",
                table: "prospectos_scrapeados",
                columns: new[] { "tenant_id", "fuente" });

            migrationBuilder.CreateIndex(
                name: "ix_prospectos_scrapeados_tercero_id",
                table: "prospectos_scrapeados",
                column: "tercero_id");

            migrationBuilder.CreateIndex(
                name: "ix_tercero_contactos_tercero_id",
                table: "tercero_contactos",
                column: "tercero_id");

            migrationBuilder.CreateIndex(
                name: "ix_tercero_field_definitions_tenant_id_ficha_key_field_key",
                table: "tercero_field_definitions",
                columns: new[] { "tenant_id", "ficha_key", "field_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tercero_field_definitions_tenant_id_ficha_key_sort_order",
                table: "tercero_field_definitions",
                columns: new[] { "tenant_id", "ficha_key", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "ix_tercero_filtros_tenant_id_sort_order",
                table: "tercero_filtros",
                columns: new[] { "tenant_id", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "ix_tercero_notas_tenant_id_tercero_id",
                table: "tercero_notas",
                columns: new[] { "tenant_id", "tercero_id" });

            migrationBuilder.CreateIndex(
                name: "ix_tercero_notas_tercero_id",
                table: "tercero_notas",
                column: "tercero_id");

            migrationBuilder.CreateIndex(
                name: "ix_terceros_bolsa_columna_id",
                table: "terceros",
                column: "bolsa_columna_id");

            migrationBuilder.CreateIndex(
                name: "ix_terceros_empresa_id",
                table: "terceros",
                column: "empresa_id");

            migrationBuilder.CreateIndex(
                name: "ix_terceros_nombre",
                table: "terceros",
                column: "nombre");

            migrationBuilder.CreateIndex(
                name: "ix_terceros_tenant_id_bolsa_columna_id",
                table: "terceros",
                columns: new[] { "tenant_id", "bolsa_columna_id" });

            migrationBuilder.CreateIndex(
                name: "ix_terceros_tenant_id_empresa_id",
                table: "terceros",
                columns: new[] { "tenant_id", "empresa_id" });

            migrationBuilder.CreateIndex(
                name: "ix_terceros_tenant_id_estado",
                table: "terceros",
                columns: new[] { "tenant_id", "estado" });

            migrationBuilder.CreateIndex(
                name: "ix_terceros_tenant_id_tipo",
                table: "terceros",
                columns: new[] { "tenant_id", "tipo" });

            migrationBuilder.AddForeignKey(
                name: "fk_task_items_actividad_subcategorias_subcategoria_id",
                table: "task_items",
                column: "subcategoria_id",
                principalTable: "actividad_subcategorias",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_task_items_entidades_entidad_id",
                table: "task_items",
                column: "entidad_id",
                principalTable: "entidades",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_task_items_project_milestones_milestone_id",
                table: "task_items",
                column: "milestone_id",
                principalTable: "project_milestones",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_task_items_actividad_subcategorias_subcategoria_id",
                table: "task_items");

            migrationBuilder.DropForeignKey(
                name: "fk_task_items_entidades_entidad_id",
                table: "task_items");

            migrationBuilder.DropForeignKey(
                name: "fk_task_items_project_milestones_milestone_id",
                table: "task_items");

            migrationBuilder.DropTable(
                name: "actividad_subcategoria_cargos");

            migrationBuilder.DropTable(
                name: "actividad_subcategoria_notificaciones");

            migrationBuilder.DropTable(
                name: "actividad_subcategoria_terceros");

            migrationBuilder.DropTable(
                name: "citas");

            migrationBuilder.DropTable(
                name: "data_container_cells");

            migrationBuilder.DropTable(
                name: "data_container_links");

            migrationBuilder.DropTable(
                name: "data_destinations");

            migrationBuilder.DropTable(
                name: "entidad_field_definitions");

            migrationBuilder.DropTable(
                name: "entidades");

            migrationBuilder.DropTable(
                name: "import_processes");

            migrationBuilder.DropTable(
                name: "item_field_definitions");

            migrationBuilder.DropTable(
                name: "notifications");

            migrationBuilder.DropTable(
                name: "project_milestones");

            migrationBuilder.DropTable(
                name: "prospectos_scrapeados");

            migrationBuilder.DropTable(
                name: "tercero_contactos");

            migrationBuilder.DropTable(
                name: "tercero_field_definitions");

            migrationBuilder.DropTable(
                name: "tercero_filtros");

            migrationBuilder.DropTable(
                name: "tercero_notas");

            migrationBuilder.DropTable(
                name: "actividad_subcategorias");

            migrationBuilder.DropTable(
                name: "oportunidades");

            migrationBuilder.DropTable(
                name: "data_container_columns");

            migrationBuilder.DropTable(
                name: "data_container_rows");

            migrationBuilder.DropTable(
                name: "data_clients");

            migrationBuilder.DropTable(
                name: "data_connectors");

            migrationBuilder.DropTable(
                name: "actividad_categorias");

            migrationBuilder.DropTable(
                name: "terceros");

            migrationBuilder.DropTable(
                name: "data_containers");

            migrationBuilder.DropTable(
                name: "bolsa_columnas");

            migrationBuilder.DropTable(
                name: "data_models");

            migrationBuilder.DropIndex(
                name: "ix_task_items_entidad_id",
                table: "task_items");

            migrationBuilder.DropIndex(
                name: "ix_task_items_milestone_id",
                table: "task_items");

            migrationBuilder.DropIndex(
                name: "ix_task_items_subcategoria_id",
                table: "task_items");

            migrationBuilder.DropIndex(
                name: "ix_task_items_tenant_id_entidad_id",
                table: "task_items");

            migrationBuilder.DropIndex(
                name: "ix_task_items_tenant_id_milestone_id",
                table: "task_items");

            migrationBuilder.DropIndex(
                name: "ix_task_items_tenant_id_subcategoria_id",
                table: "task_items");

            migrationBuilder.DropColumn(
                name: "entidad_id",
                table: "task_items");

            migrationBuilder.DropColumn(
                name: "milestone_id",
                table: "task_items");

            migrationBuilder.DropColumn(
                name: "subcategoria_id",
                table: "task_items");

            migrationBuilder.DropColumn(
                name: "is_responsible",
                table: "org_unit_members");

            migrationBuilder.DropColumn(
                name: "is_process_group",
                table: "menu_nodes");

            migrationBuilder.DropColumn(
                name: "datos_tienda_json",
                table: "items");

            migrationBuilder.DropColumn(
                name: "es_principal",
                table: "item_images");

            migrationBuilder.DropColumn(
                name: "texto",
                table: "item_images");

            migrationBuilder.AlterColumn<Guid>(
                name: "activity_type_id",
                table: "task_items",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);
        }
    }
}
