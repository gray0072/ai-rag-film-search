using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FilmSearch.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class I19_AddMessageFallback : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "fallback",
                schema: "assistant",
                table: "messages",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "fallback",
                schema: "assistant",
                table: "messages");
        }
    }
}
