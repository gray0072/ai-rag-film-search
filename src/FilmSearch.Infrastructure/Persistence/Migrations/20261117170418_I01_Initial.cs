using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FilmSearch.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class I01_Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:pg_trgm", ",,")
                .Annotation("Npgsql:PostgresExtension:vector", ",,");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
