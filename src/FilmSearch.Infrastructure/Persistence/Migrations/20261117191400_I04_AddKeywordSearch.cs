using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NpgsqlTypes;

#nullable disable

namespace FilmSearch.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class I04_AddKeywordSearch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "search");

            migrationBuilder.CreateTable(
                name: "movie_documents",
                schema: "search",
                columns: table => new
                {
                    movie_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title_en = table.Column<string>(type: "text", nullable: false),
                    title_ru = table.Column<string>(type: "text", nullable: false),
                    title_sv = table.Column<string>(type: "text", nullable: false),
                    original_title = table.Column<string>(type: "text", nullable: false),
                    keywords = table.Column<string>(type: "text", nullable: false, defaultValue: ""),
                    people = table.Column<string>(type: "text", nullable: false, defaultValue: ""),
                    tagline_en = table.Column<string>(type: "text", nullable: true),
                    tagline_ru = table.Column<string>(type: "text", nullable: true),
                    tagline_sv = table.Column<string>(type: "text", nullable: true),
                    overview_en = table.Column<string>(type: "text", nullable: true),
                    overview_ru = table.Column<string>(type: "text", nullable: true),
                    overview_sv = table.Column<string>(type: "text", nullable: true),
                    search_en = table.Column<NpgsqlTsVector>(type: "tsvector", nullable: false, computedColumnSql: "setweight(to_tsvector('english'::regconfig, translate(title_en || ' ' || original_title, 'Ёё', 'Ее')), 'A') || setweight(to_tsvector('english'::regconfig, translate(keywords || ' ' || people, 'Ёё', 'Ее')), 'B') || setweight(to_tsvector('english'::regconfig, translate(coalesce(tagline_en, ''), 'Ёё', 'Ее')), 'C') || setweight(to_tsvector('english'::regconfig, translate(coalesce(overview_en, ''), 'Ёё', 'Ее')), 'D')", stored: true),
                    search_ru = table.Column<NpgsqlTsVector>(type: "tsvector", nullable: false, computedColumnSql: "setweight(to_tsvector('russian'::regconfig, translate(title_ru || ' ' || original_title, 'Ёё', 'Ее')), 'A') || setweight(to_tsvector('russian'::regconfig, translate(keywords || ' ' || people, 'Ёё', 'Ее')), 'B') || setweight(to_tsvector('russian'::regconfig, translate(coalesce(tagline_ru, ''), 'Ёё', 'Ее')), 'C') || setweight(to_tsvector('russian'::regconfig, translate(coalesce(overview_ru, ''), 'Ёё', 'Ее')), 'D')", stored: true),
                    search_sv = table.Column<NpgsqlTsVector>(type: "tsvector", nullable: false, computedColumnSql: "setweight(to_tsvector('swedish'::regconfig, translate(title_sv || ' ' || original_title, 'Ёё', 'Ее')), 'A') || setweight(to_tsvector('swedish'::regconfig, translate(keywords || ' ' || people, 'Ёё', 'Ее')), 'B') || setweight(to_tsvector('swedish'::regconfig, translate(coalesce(tagline_sv, ''), 'Ёё', 'Ее')), 'C') || setweight(to_tsvector('swedish'::regconfig, translate(coalesce(overview_sv, ''), 'Ёё', 'Ее')), 'D')", stored: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_movie_documents", x => x.movie_id);
                    table.ForeignKey(
                        name: "fk_movie_documents_movies_movie_id",
                        column: x => x.movie_id,
                        principalSchema: "catalog",
                        principalTable: "movies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_movie_documents_search_en",
                schema: "search",
                table: "movie_documents",
                column: "search_en")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "ix_movie_documents_search_ru",
                schema: "search",
                table: "movie_documents",
                column: "search_ru")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "ix_movie_documents_search_sv",
                schema: "search",
                table: "movie_documents",
                column: "search_sv")
                .Annotation("Npgsql:IndexMethod", "gin");

            // Flattens catalog rows into search documents (English fallback per field). Called by the catalog
            // writer for created / changed movies (MovieDocumentIndexer); NULL rebuilds all documents.
            migrationBuilder.Sql("""
                CREATE FUNCTION search.refresh_movie_documents(movie_ids uuid[])
                RETURNS integer
                LANGUAGE sql
                AS $$
                    WITH upserted AS (
                        INSERT INTO search.movie_documents AS d (
                            movie_id, title_en, title_ru, title_sv, original_title, keywords, people,
                            tagline_en, tagline_ru, tagline_sv, overview_en, overview_ru, overview_sv)
                        SELECT
                            m.id,
                            m.title,
                            coalesce(ru.title, m.title),
                            coalesce(sv.title, m.title),
                            m.original_title,
                            array_to_string(m.keywords, ' '),
                            coalesce((
                                SELECT string_agg(p.name, ' ' ORDER BY c.role, c."order")
                                FROM catalog.movie_credits c
                                JOIN catalog.people p ON p.id = c.person_id
                                WHERE c.movie_id = m.id), ''),
                            m.tagline,
                            coalesce(ru.tagline, m.tagline),
                            coalesce(sv.tagline, m.tagline),
                            m.overview,
                            coalesce(ru.overview, m.overview),
                            coalesce(sv.overview, m.overview)
                        FROM catalog.movies m
                        LEFT JOIN catalog.movie_translations ru ON ru.movie_id = m.id AND ru.locale = 'ru'
                        LEFT JOIN catalog.movie_translations sv ON sv.movie_id = m.id AND sv.locale = 'sv'
                        WHERE movie_ids IS NULL OR m.id = ANY (movie_ids)
                        ON CONFLICT (movie_id) DO UPDATE SET
                            title_en = excluded.title_en,
                            title_ru = excluded.title_ru,
                            title_sv = excluded.title_sv,
                            original_title = excluded.original_title,
                            keywords = excluded.keywords,
                            people = excluded.people,
                            tagline_en = excluded.tagline_en,
                            tagline_ru = excluded.tagline_ru,
                            tagline_sv = excluded.tagline_sv,
                            overview_en = excluded.overview_en,
                            overview_ru = excluded.overview_ru,
                            overview_sv = excluded.overview_sv
                        RETURNING 1)
                    SELECT count(*)::integer FROM upserted;
                $$;
                """);

            // Documents for the movies that are already in the catalog.
            migrationBuilder.Sql("SELECT search.refresh_movie_documents(NULL);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP FUNCTION search.refresh_movie_documents(uuid[]);");

            migrationBuilder.DropTable(
                name: "movie_documents",
                schema: "search");
        }
    }
}
