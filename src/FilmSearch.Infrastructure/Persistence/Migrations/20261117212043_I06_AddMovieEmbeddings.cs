using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Pgvector;

#nullable disable

namespace FilmSearch.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class I06_AddMovieEmbeddings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "movie_embeddings",
                schema: "search",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    movie_id = table.Column<Guid>(type: "uuid", nullable: false),
                    model = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    dimensions = table.Column<int>(type: "integer", nullable: false),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    locale = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    text = table.Column<string>(type: "text", nullable: false),
                    content_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    vector = table.Column<Vector>(type: "vector", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_movie_embeddings", x => x.id);
                    table.ForeignKey(
                        name: "fk_movie_embeddings_movies_movie_id",
                        column: x => x.movie_id,
                        principalSchema: "catalog",
                        principalTable: "movies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_movie_embeddings_model_locale",
                schema: "search",
                table: "movie_embeddings",
                columns: new[] { "model", "locale" });

            migrationBuilder.CreateIndex(
                name: "ix_movie_embeddings_movie_id_model_kind_locale",
                schema: "search",
                table: "movie_embeddings",
                columns: new[] { "movie_id", "model", "kind", "locale" },
                unique: true);

            // ADR-0008: the vector column has no dimension, so the HNSW index is a partial expression index per dimension.
            // This one serves the default 768-dimension model; the embedding indexer creates the same index for other
            // dimensions on first use (VectorIndex.CreateSql). Literal SQL: a migration must not change with the code.
            migrationBuilder.Sql(
                "CREATE INDEX IF NOT EXISTS ix_movie_embeddings_vector_768 ON search.movie_embeddings " +
                "USING hnsw ((vector::vector(768)) vector_cosine_ops) WITH (m = 16, ef_construction = 64) " +
                "WHERE dimensions = 768");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "movie_embeddings",
                schema: "search");
        }
    }
}
