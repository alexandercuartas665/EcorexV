using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.SqlServer.Migrations
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
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    model_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    from_table_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    to_table_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    kind = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
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

            // ---- BACKFILL (equivalente SQL Server de la variante Postgres) ----
            migrationBuilder.Sql(@"
                INSERT INTO data_model_relations (id, tenant_id, model_id, from_table_id, to_table_id, kind, name, created_at)
                SELECT NEWID(), c.tenant_id, src.model_id, c.container_id, c.referenced_container_id,
                       CASE c.type WHEN 'RelationMany' THEN 'ManyToMany' ELSE 'ManyToOne' END,
                       c.name, SYSUTCDATETIME()
                FROM data_container_columns c
                JOIN data_containers src ON src.id = c.container_id
                JOIN data_containers tgt ON tgt.id = c.referenced_container_id
                WHERE c.type IN ('Reference','RelationMany')
                  AND c.referenced_container_id IS NOT NULL
                  AND src.model_id IS NOT NULL
                  AND tgt.model_id = src.model_id;");

            migrationBuilder.Sql(@"DELETE FROM data_container_cells WHERE column_id IN (SELECT id FROM data_container_columns WHERE type IN ('Reference','RelationMany'));");
            migrationBuilder.Sql(@"DELETE FROM data_container_links WHERE column_id IN (SELECT id FROM data_container_columns WHERE type IN ('Reference','RelationMany'));");
            migrationBuilder.Sql(@"DELETE FROM data_container_columns WHERE type IN ('Reference','RelationMany');");

            migrationBuilder.DropForeignKey(
                name: "fk_data_container_columns_data_containers_referenced_container_id",
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
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_data_container_columns_referenced_container_id",
                table: "data_container_columns",
                column: "referenced_container_id");

            migrationBuilder.AddForeignKey(
                name: "fk_data_container_columns_data_containers_referenced_container_id",
                table: "data_container_columns",
                column: "referenced_container_id",
                principalTable: "data_containers",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
