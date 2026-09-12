using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddNodeNotePosition : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "note_offset_x",
                table: "workflow_nodes",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "note_offset_y",
                table: "workflow_nodes",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "note_offset_x",
                table: "workflow_nodes");

            migrationBuilder.DropColumn(
                name: "note_offset_y",
                table: "workflow_nodes");
        }
    }
}
