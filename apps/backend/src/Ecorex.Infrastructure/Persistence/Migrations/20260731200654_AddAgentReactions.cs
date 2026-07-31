using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecorex.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAgentReactions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "reaction_emojis",
                table: "ai_agents",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "reaction_ratio_m",
                table: "ai_agents",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "reaction_ratio_n",
                table: "ai_agents",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "reactions_enabled",
                table: "ai_agents",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Frecuencia por defecto para agentes ya existentes (3 de cada 4). Las reacciones siguen apagadas.
            migrationBuilder.Sql("UPDATE ai_agents SET reaction_ratio_n = 3, reaction_ratio_m = 4 WHERE reaction_ratio_m = 0;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "reaction_emojis",
                table: "ai_agents");

            migrationBuilder.DropColumn(
                name: "reaction_ratio_m",
                table: "ai_agents");

            migrationBuilder.DropColumn(
                name: "reaction_ratio_n",
                table: "ai_agents");

            migrationBuilder.DropColumn(
                name: "reactions_enabled",
                table: "ai_agents");
        }
    }
}
