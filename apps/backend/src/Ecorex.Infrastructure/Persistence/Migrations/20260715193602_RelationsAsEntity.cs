using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RelationsAsEntity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "data_model_relations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    model_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_table_id = table.Column<Guid>(type: "uuid", nullable: false),
                    to_table_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_data_model_relations", x => x.id);
                    table.ForeignKey(
                        name: "fk_data_model_relations_data_containers_from_table_id",
                        column: x => x.from_table_id,
                        principalTable: "data_containers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_data_model_relations_data_containers_to_table_id",
                        column: x => x.to_table_id,
                        principalTable: "data_containers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_data_model_relations_data_models_model_id",
                        column: x => x.model_id,
                        principalTable: "data_models",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_data_model_relations_from_table_id",
                table: "data_model_relations",
                column: "from_table_id");

            migrationBuilder.CreateIndex(
                name: "ix_data_model_relations_model_id",
                table: "data_model_relations",
                column: "model_id");

            migrationBuilder.CreateIndex(
                name: "ix_data_model_relations_tenant_id_model_id",
                table: "data_model_relations",
                columns: new[] { "tenant_id", "model_id" });

            migrationBuilder.CreateIndex(
                name: "ix_data_model_relations_to_table_id",
                table: "data_model_relations",
                column: "to_table_id");

            // ---- BACKFILL: convertir las relaciones-columna existentes en aristas (DataModelRelation) ----
            // Cada columna Reference/RelationMany con destino intra-modelo pasa a ser una fila de la nueva
            // tabla. Se corre ANTES de dropear referenced_container_id (que aun se necesita para leerlo).
            migrationBuilder.Sql(@"
                INSERT INTO data_model_relations (id, tenant_id, model_id, from_table_id, to_table_id, kind, name, created_at)
                SELECT gen_random_uuid(), c.tenant_id, src.model_id, c.container_id, c.referenced_container_id,
                       CASE c.type WHEN 'RelationMany' THEN 'ManyToMany' ELSE 'ManyToOne' END,
                       c.name, now()
                FROM data_container_columns c
                JOIN data_containers src ON src.id = c.container_id
                JOIN data_containers tgt ON tgt.id = c.referenced_container_id
                WHERE c.type IN ('Reference','RelationMany')
                  AND c.referenced_container_id IS NOT NULL
                  AND src.model_id IS NOT NULL
                  AND tgt.model_id = src.model_id;");

            // Neutralizar las columnas de relacion (sus celdas y vinculos de fila se pierden: el esquema de
            // relacion se preserva como arista, el vinculo dato-a-dato se re-cableara en una fase posterior).
            migrationBuilder.Sql(@"DELETE FROM data_container_cells WHERE column_id IN (SELECT id FROM data_container_columns WHERE type IN ('Reference','RelationMany'));");
            migrationBuilder.Sql(@"DELETE FROM data_container_links WHERE column_id IN (SELECT id FROM data_container_columns WHERE type IN ('Reference','RelationMany'));");
            migrationBuilder.Sql(@"DELETE FROM data_container_columns WHERE type IN ('Reference','RelationMany');");

            // Ya sin columnas de relacion: se dropea la FK/indice/columna referenced_container_id.
            migrationBuilder.DropForeignKey(
                name: "fk_data_container_columns_data_containers_referenced_container",
                table: "data_container_columns");

            migrationBuilder.DropIndex(
                name: "ix_data_container_columns_referenced_container_id",
                table: "data_container_columns");

            migrationBuilder.DropColumn(
                name: "referenced_container_id",
                table: "data_container_columns");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "data_model_relations");

            migrationBuilder.AddColumn<Guid>(
                name: "referenced_container_id",
                table: "data_container_columns",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_data_container_columns_referenced_container_id",
                table: "data_container_columns",
                column: "referenced_container_id");

            migrationBuilder.AddForeignKey(
                name: "fk_data_container_columns_data_containers_referenced_container",
                table: "data_container_columns",
                column: "referenced_container_id",
                principalTable: "data_containers",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
