using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FilmSearch.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class I11_AddAssistant : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "assistant");

            migrationBuilder.CreateTable(
                name: "conversations",
                schema: "assistant",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(81)", maxLength: 81, nullable: true),
                    locale = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    profile_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    total_input_tokens = table.Column<long>(type: "bigint", nullable: false),
                    total_output_tokens = table.Column<long>(type: "bigint", nullable: false),
                    estimated_cost_usd = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: false),
                    summary = table.Column<string>(type: "text", nullable: true),
                    summarized_through_seq = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_conversations", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "messages",
                schema: "assistant",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    conversation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    seq = table.Column<int>(type: "integer", nullable: false),
                    role = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    content = table.Column<string>(type: "text", nullable: false),
                    tool_calls_json = table.Column<string>(type: "jsonb", nullable: true),
                    tool_call_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    model = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    input_tokens = table.Column<long>(type: "bigint", nullable: true),
                    output_tokens = table.Column<long>(type: "bigint", nullable: true),
                    estimated_cost_usd = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: true),
                    latency_ms = table.Column<int>(type: "integer", nullable: true),
                    prompt_version = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    movie_ids = table.Column<List<Guid>>(type: "uuid[]", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_messages", x => x.id);
                    table.ForeignKey(
                        name: "fk_messages_conversations_conversation_id",
                        column: x => x.conversation_id,
                        principalSchema: "assistant",
                        principalTable: "conversations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "message_feedback",
                schema: "assistant",
                columns: table => new
                {
                    message_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rating = table.Column<int>(type: "integer", nullable: false),
                    comment = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_message_feedback", x => x.message_id);
                    table.CheckConstraint("ck_message_feedback_rating", "rating IN (-1, 1)");
                    table.ForeignKey(
                        name: "fk_message_feedback_messages_message_id",
                        column: x => x.message_id,
                        principalSchema: "assistant",
                        principalTable: "messages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_conversations_owner_id_updated_at",
                schema: "assistant",
                table: "conversations",
                columns: new[] { "owner_id", "updated_at" });

            migrationBuilder.CreateIndex(
                name: "ix_messages_conversation_id_seq",
                schema: "assistant",
                table: "messages",
                columns: new[] { "conversation_id", "seq" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "message_feedback",
                schema: "assistant");

            migrationBuilder.DropTable(
                name: "messages",
                schema: "assistant");

            migrationBuilder.DropTable(
                name: "conversations",
                schema: "assistant");
        }
    }
}
