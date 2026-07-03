using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddHairLengthClassification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "hair_length_classifications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    photo_file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    predicted_category_id = table.Column<Guid>(type: "uuid", nullable: true),
                    predicted_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    confidence = table.Column<int>(type: "integer", nullable: false),
                    rationale = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_hair_length_classifications", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_hair_length_classifications_tenant_id_created_at",
                table: "hair_length_classifications",
                columns: new[] { "tenant_id", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "hair_length_classifications");
        }
    }
}
