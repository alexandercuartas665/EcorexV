using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddActividadConceptos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "actividad_categorias",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    codigo = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    nombre = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    descripcion = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: true),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    is_archived = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_actividad_categorias", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "actividad_subcategorias",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    categoria_id = table.Column<Guid>(type: "uuid", nullable: false),
                    codigo = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    nombre = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    chequeo = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    descripcion = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    is_archived = table.Column<bool>(type: "boolean", nullable: false),
                    requiere_cliente = table.Column<bool>(type: "boolean", nullable: false),
                    inicia_modulo = table.Column<bool>(type: "boolean", nullable: false),
                    cierre_manual = table.Column<bool>(type: "boolean", nullable: false),
                    titulo_auto = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    detalle_auto = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    workflow_definition_id = table.Column<Guid>(type: "uuid", nullable: true),
                    form_definition_id = table.Column<Guid>(type: "uuid", nullable: true),
                    task_board_id = table.Column<Guid>(type: "uuid", nullable: true),
                    task_board_column_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
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
                        name: "fk_actividad_subcategorias_task_board_columns_task_board_colum",
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
                        name: "fk_actividad_subcategorias_workflow_definitions_workflow_defin",
                        column: x => x.workflow_definition_id,
                        principalTable: "workflow_definitions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "actividad_subcategoria_cargos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    subcategoria_id = table.Column<Guid>(type: "uuid", nullable: false),
                    org_unit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_actividad_subcategoria_cargos", x => x.id);
                    table.ForeignKey(
                        name: "fk_actividad_subcategoria_cargos_actividad_subcategorias_subca",
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
                name: "actividad_subcategoria_terceros",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    subcategoria_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tercero_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_actividad_subcategoria_terceros", x => x.id);
                    table.ForeignKey(
                        name: "fk_actividad_subcategoria_terceros_actividad_subcategorias_sub",
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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "actividad_subcategoria_cargos");

            migrationBuilder.DropTable(
                name: "actividad_subcategoria_terceros");

            migrationBuilder.DropTable(
                name: "actividad_subcategorias");

            migrationBuilder.DropTable(
                name: "actividad_categorias");
        }
    }
}
