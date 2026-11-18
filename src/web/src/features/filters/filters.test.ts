import { describe, expect, it } from "vitest";
import {
  filterChips,
  fromSearchFilters,
  parseFilters,
  parsePage,
  parseSort,
  removeChip,
  toListQuery,
  toSearchFilters,
  writeFilters,
} from "./filters";

describe("filters in the URL", () => {
  it("parses every filter and ignores invalid values", () => {
    const filters = parseFilters(
      new URLSearchParams(
        "genres=drama,%20comedy&yearFrom=1990&yearTo=abc&minRating=8&language=JA&priceMax=499&offerType=rent&runtimeMax=",
      ),
    );

    expect(filters).toEqual({
      genres: ["drama", "comedy"],
      excludeGenres: [],
      yearFrom: 1990,
      minRating: 8,
      language: "ja",
      priceMax: 499,
      offerType: "rent",
    });
    expect(parseFilters(new URLSearchParams("offerType=lease")).offerType).toBeUndefined();
  });

  it("writes filters, keeps other keys and resets the page", () => {
    const params = writeFilters(new URLSearchParams("q=space&sort=rating&page=3&yearTo=1980"), {
      genres: ["drama", "crime"],
      excludeGenres: ["action"],
      yearFrom: 1990,
      offerType: "buy",
    });

    expect(params.toString()).toBe(
      "q=space&sort=rating&genres=drama%2Ccrime&excludeGenres=action&yearFrom=1990&offerType=buy",
    );
    expect(parseFilters(params)).toEqual({
      genres: ["drama", "crime"],
      excludeGenres: ["action"],
      yearFrom: 1990,
      offerType: "buy",
    });
  });

  it("falls back to defaults for sort and page", () => {
    expect(parseSort("rating")).toBe("rating");
    expect(parseSort("RATING")).toBe("popularity");
    expect(parseSort(null)).toBe("popularity");
    expect(parsePage("2")).toBe(2);
    expect(parsePage("0")).toBe(1);
    expect(parsePage("1.5")).toBe(1);
    expect(parsePage(null)).toBe(1);
  });
});

describe("filters for the API", () => {
  const filters = {
    genres: ["drama"],
    excludeGenres: ["action"],
    yearFrom: 1990,
    yearTo: 2000,
    minRating: 8,
  };

  it("builds the catalog query without the search-only excluded genres", () => {
    expect(toListQuery(filters, "rating", 2)).toEqual({
      page: 2,
      pageSize: 20,
      sort: "rating",
      genres: "drama",
      yearFrom: 1990,
      yearTo: 2000,
      minRating: 8,
    });
  });

  it("round-trips through search filters", () => {
    const search = toSearchFilters(filters);

    expect(search).toMatchObject({ genres: ["drama"], excludeGenres: ["action"], priceMax: null });
    expect(fromSearchFilters(search)).toEqual(filters);
  });
});

describe("filter chips", () => {
  it("makes one chip per value and removes exactly that value", () => {
    const filters = { genres: ["drama", "crime"], excludeGenres: [], minRating: 8 };
    const chips = filterChips(filters);

    expect(chips).toEqual([
      { kind: "genre", slug: "drama" },
      { kind: "genre", slug: "crime" },
      { kind: "minRating", value: 8 },
    ]);
    expect(removeChip(filters, { kind: "genre", slug: "drama" }).genres).toEqual(["crime"]);
    expect(filterChips(removeChip(filters, { kind: "minRating", value: 8 }))).toHaveLength(2);
  });
});
