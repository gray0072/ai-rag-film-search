using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FilmSearch.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class I16_AddIngestionJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "ingestion");

            migrationBuilder.CreateTable(
                name: "jobs",
                schema: "ingestion",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    state = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    trigger = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    parameters = table.Column<string>(type: "jsonb", nullable: false),
                    progress = table.Column<string>(type: "jsonb", nullable: true),
                    result = table.Column<string>(type: "jsonb", nullable: true),
                    error = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    cancel_requested = table.Column<bool>(type: "boolean", nullable: false),
                    worker_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    lease_id = table.Column<Guid>(type: "uuid", nullable: true),
                    queued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    heartbeat_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_jobs", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_jobs_kind_queued_at",
                schema: "ingestion",
                table: "jobs",
                columns: new[] { "kind", "queued_at" });

            migrationBuilder.CreateIndex(
                name: "ix_jobs_state_queued_at",
                schema: "ingestion",
                table: "jobs",
                columns: new[] { "state", "queued_at" });

            migrationBuilder.CreateIndex(
                name: "ux_jobs_one_active_per_kind",
                schema: "ingestion",
                table: "jobs",
                column: "kind",
                unique: true,
                filter: "state IN ('Queued', 'Running')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "jobs",
                schema: "ingestion");
        }
    }
}
