using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FilmSearch.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class I03_AddCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "catalog");

            migrationBuilder.CreateTable(
                name: "genres",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    slug = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    name_en = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    name_ru = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    name_sv = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_genres", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "movies",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tmdb_id = table.Column<int>(type: "integer", nullable: true),
                    imdb_id = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    original_title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    original_language = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    release_date = table.Column<DateOnly>(type: "date", nullable: true),
                    year = table.Column<int>(type: "integer", nullable: true),
                    runtime_minutes = table.Column<int>(type: "integer", nullable: true),
                    overview = table.Column<string>(type: "text", nullable: true),
                    tagline = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    poster_path = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    backdrop_path = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    vote_average = table.Column<decimal>(type: "numeric(3,1)", precision: 3, scale: 1, nullable: false),
                    vote_count = table.Column<int>(type: "integer", nullable: false),
                    popularity = table.Column<double>(type: "double precision", nullable: false),
                    keywords = table.Column<List<string>>(type: "text[]", nullable: false),
                    age_rating = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    source = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    source_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_movies", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "people",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tmdb_id = table.Column<int>(type: "integer", nullable: true),
                    name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_people", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "movie_genres",
                schema: "catalog",
                columns: table => new
                {
                    movie_id = table.Column<Guid>(type: "uuid", nullable: false),
                    genre_id = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_movie_genres", x => new { x.movie_id, x.genre_id });
                    table.ForeignKey(
                        name: "fk_movie_genres_genres_genre_id",
                        column: x => x.genre_id,
                        principalSchema: "catalog",
                        principalTable: "genres",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_movie_genres_movies_movie_id",
                        column: x => x.movie_id,
                        principalSchema: "catalog",
                        principalTable: "movies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "movie_translations",
                schema: "catalog",
                columns: table => new
                {
                    movie_id = table.Column<Guid>(type: "uuid", nullable: false),
                    locale = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    overview = table.Column<string>(type: "text", nullable: true),
                    tagline = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_movie_translations", x => new { x.movie_id, x.locale });
                    table.ForeignKey(
                        name: "fk_movie_translations_movies_movie_id",
                        column: x => x.movie_id,
                        principalSchema: "catalog",
                        principalTable: "movies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "offers",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    movie_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    price_minor = table.Column<long>(type: "bigint", nullable: false),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    rental_hours = table.Column<int>(type: "integer", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_offers", x => x.id);
                    table.ForeignKey(
                        name: "fk_offers_movies_movie_id",
                        column: x => x.movie_id,
                        principalSchema: "catalog",
                        principalTable: "movies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "movie_credits",
                schema: "catalog",
                columns: table => new
                {
                    movie_id = table.Column<Guid>(type: "uuid", nullable: false),
                    person_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    character = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_movie_credits", x => new { x.movie_id, x.person_id, x.role });
                    table.ForeignKey(
                        name: "fk_movie_credits_movies_movie_id",
                        column: x => x.movie_id,
                        principalSchema: "catalog",
                        principalTable: "movies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_movie_credits_person_person_id",
                        column: x => x.person_id,
                        principalSchema: "catalog",
                        principalTable: "people",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_genres_slug",
                schema: "catalog",
                table: "genres",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_movie_credits_person_id",
                schema: "catalog",
                table: "movie_credits",
                column: "person_id");

            migrationBuilder.CreateIndex(
                name: "ix_movie_genres_genre_id",
                schema: "catalog",
                table: "movie_genres",
                column: "genre_id");

            migrationBuilder.CreateIndex(
                name: "ix_movies_original_language",
                schema: "catalog",
                table: "movies",
                column: "original_language");

            migrationBuilder.CreateIndex(
                name: "ix_movies_popularity",
                schema: "catalog",
                table: "movies",
                column: "popularity");

            migrationBuilder.CreateIndex(
                name: "ix_movies_tmdb_id",
                schema: "catalog",
                table: "movies",
                column: "tmdb_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_movies_vote_average",
                schema: "catalog",
                table: "movies",
                column: "vote_average");

            migrationBuilder.CreateIndex(
                name: "ix_movies_year",
                schema: "catalog",
                table: "movies",
                column: "year");

            migrationBuilder.CreateIndex(
                name: "ix_offers_movie_id_type",
                schema: "catalog",
                table: "offers",
                columns: new[] { "movie_id", "type" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_people_name",
                schema: "catalog",
                table: "people",
                column: "name");

            migrationBuilder.CreateIndex(
                name: "ix_people_tmdb_id",
                schema: "catalog",
                table: "people",
                column: "tmdb_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "movie_credits",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "movie_genres",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "movie_translations",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "offers",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "people",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "genres",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "movies",
                schema: "catalog");
        }
    }
}
