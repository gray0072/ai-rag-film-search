/** Poster widths offered by the TMDB image CDN that the UI uses. */
export type PosterSize = "w185" | "w342" | "w500";

/** The API stores TMDB poster paths only (spec 08); the image URL is built here. */
export function posterUrl(posterPath: string | null | undefined, size: PosterSize = "w342") {
  return posterPath ? `https://image.tmdb.org/t/p/${size}${posterPath}` : undefined;
}

export const tmdbSiteUrl = "https://www.themoviedb.org/";
