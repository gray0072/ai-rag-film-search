import type { operations } from "@/api/schema";
import type { OfferType, SearchFilters } from "@/api/types";

/**
 * Catalog filters (FR-CAT-2) as the UI keeps them. The URL query string is the single source of truth
 * (shareable links, back button), so the pages parse it on every render.
 */
export interface MovieFilters {
  genres: string[];
  /** Search only: genres the query understanding excluded ("no action"). */
  excludeGenres: string[];
  yearFrom?: number;
  yearTo?: number;
  minRating?: number;
  runtimeMin?: number;
  runtimeMax?: number;
  /** Original language, ISO 639-1. */
  language?: string;
  /** Minor units. */
  priceMax?: number;
  offerType?: OfferType;
}

export const emptyFilters: MovieFilters = { genres: [], excludeGenres: [] };

export const catalogSorts = ["popularity", "rating", "year", "title"] as const;
export type CatalogSort = (typeof catalogSorts)[number];

export const pageSize = 20;

/** Original languages offered in the filter: the most common ones in the catalog. */
export const filterLanguages = [
  "en",
  "ja",
  "fr",
  "it",
  "es",
  "ko",
  "de",
  "zh",
  "pt",
  "sv",
  "ru",
  "hi",
] as const;

export type NumericFilterKey =
  "yearFrom" | "yearTo" | "minRating" | "runtimeMin" | "runtimeMax" | "priceMax";

const numericKeys: readonly NumericFilterKey[] = [
  "yearFrom",
  "yearTo",
  "minRating",
  "runtimeMin",
  "runtimeMax",
  "priceMax",
];

/** Every query string key that belongs to the filters. */
export const filterKeys: readonly string[] = [
  "genres",
  "excludeGenres",
  "language",
  "offerType",
  ...numericKeys,
];

function parseList(value: string | null): string[] {
  return (value ?? "")
    .split(",")
    .map((item) => item.trim())
    .filter(Boolean);
}

function parseNumber(value: string | null): number | undefined {
  if (value === null || value.trim() === "") {
    return undefined;
  }
  const number = Number(value);
  return Number.isFinite(number) ? number : undefined;
}

function isOfferType(value: string | null): value is OfferType {
  return value === "buy" || value === "rent";
}

/** Reads filters from a query string; invalid values are ignored rather than sent to the API. */
export function parseFilters(params: URLSearchParams): MovieFilters {
  const filters: MovieFilters = {
    genres: parseList(params.get("genres")),
    excludeGenres: parseList(params.get("excludeGenres")),
  };
  for (const key of numericKeys) {
    const value = parseNumber(params.get(key));
    if (value !== undefined) {
      filters[key] = value;
    }
  }
  const language = params.get("language")?.trim().toLowerCase();
  if (language) {
    filters.language = language;
  }
  const offerType = params.get("offerType");
  if (isOfferType(offerType)) {
    filters.offerType = offerType;
  }
  return filters;
}

/** Writes filters into a copy of the query string; other keys (q, sort, mode) are kept, the page is reset. */
export function writeFilters(params: URLSearchParams, filters: MovieFilters): URLSearchParams {
  const next = new URLSearchParams(params);
  for (const key of filterKeys) {
    next.delete(key);
  }
  next.delete("page");
  if (filters.genres.length > 0) {
    next.set("genres", filters.genres.join(","));
  }
  if (filters.excludeGenres.length > 0) {
    next.set("excludeGenres", filters.excludeGenres.join(","));
  }
  for (const key of numericKeys) {
    const value = filters[key];
    if (value !== undefined) {
      next.set(key, String(value));
    }
  }
  if (filters.language) {
    next.set("language", filters.language);
  }
  if (filters.offerType) {
    next.set("offerType", filters.offerType);
  }
  return next;
}

export function hasFilters(filters: MovieFilters): boolean {
  return filterChips(filters).length > 0;
}

export function parseSort(value: string | null): CatalogSort {
  return catalogSorts.find((sort) => sort === value) ?? "popularity";
}

export function parsePage(value: string | null): number {
  const page = Number(value);
  return Number.isInteger(page) && page >= 1 ? Math.min(page, 10_000) : 1;
}

type ListQuery = NonNullable<operations["ListMovies"]["parameters"]["query"]>;

/** Query of `GET /api/movies`. Excluded genres are a search-only filter. */
export function toListQuery(filters: MovieFilters, sort: CatalogSort, page: number): ListQuery {
  const query: ListQuery = { page, pageSize, sort };
  if (filters.genres.length > 0) {
    query.genres = filters.genres.join(",");
  }
  for (const key of numericKeys) {
    const value = filters[key];
    if (value !== undefined) {
      query[key] = value;
    }
  }
  if (filters.language) {
    query.language = filters.language;
  }
  if (filters.offerType) {
    query.offerType = filters.offerType;
  }
  return query;
}

/** Filters of `POST /api/search` (FR-SRCH-5: every catalog filter works with every mode). */
export function toSearchFilters(filters: MovieFilters): SearchFilters {
  return {
    genres: filters.genres.length > 0 ? filters.genres : null,
    excludeGenres: filters.excludeGenres.length > 0 ? filters.excludeGenres : null,
    yearFrom: filters.yearFrom ?? null,
    yearTo: filters.yearTo ?? null,
    minRating: filters.minRating ?? null,
    runtimeMin: filters.runtimeMin ?? null,
    runtimeMax: filters.runtimeMax ?? null,
    language: filters.language ?? null,
    priceMax: filters.priceMax ?? null,
    offerType: filters.offerType ?? null,
  };
}

/** The filters the API applied (request filters merged with the understood ones) back in UI form. */
export function fromSearchFilters(applied: SearchFilters): MovieFilters {
  const filters: MovieFilters = {
    genres: applied.genres ?? [],
    excludeGenres: applied.excludeGenres ?? [],
  };
  for (const key of numericKeys) {
    const value = applied[key];
    if (value !== null && value !== undefined) {
      filters[key] = value;
    }
  }
  if (applied.language) {
    filters.language = applied.language;
  }
  if (applied.offerType) {
    filters.offerType = applied.offerType;
  }
  return filters;
}

/** One removable chip per applied filter value. */
export type FilterChip =
  | { kind: "genre"; slug: string }
  | { kind: "excludeGenre"; slug: string }
  | { kind: NumericFilterKey; value: number }
  | { kind: "language"; value: string }
  | { kind: "offerType"; value: OfferType };

export function filterChips(filters: MovieFilters): FilterChip[] {
  const chips: FilterChip[] = [
    ...filters.genres.map((slug): FilterChip => ({ kind: "genre", slug })),
    ...filters.excludeGenres.map((slug): FilterChip => ({ kind: "excludeGenre", slug })),
  ];
  for (const key of numericKeys) {
    const value = filters[key];
    if (value !== undefined) {
      chips.push({ kind: key, value });
    }
  }
  if (filters.language) {
    chips.push({ kind: "language", value: filters.language });
  }
  if (filters.offerType) {
    chips.push({ kind: "offerType", value: filters.offerType });
  }
  return chips;
}

export function chipKey(chip: FilterChip): string {
  return "slug" in chip ? `${chip.kind}:${chip.slug}` : `${chip.kind}:${String(chip.value)}`;
}

export function removeChip(filters: MovieFilters, chip: FilterChip): MovieFilters {
  switch (chip.kind) {
    case "genre":
      return { ...filters, genres: filters.genres.filter((slug) => slug !== chip.slug) };
    case "excludeGenre":
      return {
        ...filters,
        excludeGenres: filters.excludeGenres.filter((slug) => slug !== chip.slug),
      };
    default:
      return { ...filters, [chip.kind]: undefined };
  }
}

/** Two chips stand for the same filter value (used to tell explicit filters from understood ones). */
export function sameChip(a: FilterChip, b: FilterChip): boolean {
  return chipKey(a) === chipKey(b);
}
