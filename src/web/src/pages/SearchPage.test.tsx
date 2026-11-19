import { screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { SearchRequest, SearchResponse } from "@/api/types";
import i18n from "@/i18n";
import { aCart, aMovie, genres, mockApi, requestsTo } from "@/test/mockApi";
import { renderApp } from "@/test/renderApp";

/** Answers like the API: the request filters plus what query understanding found (when it is on). */
function stubSearch(understood?: {
  genres?: string[];
  excludeGenres?: string[];
  yearFrom?: number;
}) {
  return mockApi({
    "GET /api/genres": genres,
    "GET /api/cart": aCart(),
    "POST /api/search": async (request: Request): Promise<SearchResponse> => {
      const body = (await request.json()) as SearchRequest;
      const filters = body.filters ?? {};
      const found = body.understand ? understood : undefined;
      return {
        items: [
          {
            movie: aMovie(),
            score: 0.0327,
            debug: body.debug
              ? {
                  keywordRank: 1,
                  keywordScore: 0.61,
                  keywordMatch: "all",
                  vectorRank: 3,
                  similarity: 0.71,
                  fusedScore: 0.0327,
                }
              : null,
          },
        ],
        appliedFilters: {
          ...filters,
          genres: [...(filters.genres ?? []), ...(found?.genres ?? [])],
          excludeGenres: [...(filters.excludeGenres ?? []), ...(found?.excludeGenres ?? [])],
          yearFrom: filters.yearFrom ?? found?.yearFrom ?? null,
        },
        understoodQuery: found
          ? {
              semanticQuery: "time travel",
              filters: {
                genresInclude: found.genres ?? [],
                genresExclude: found.excludeGenres ?? [],
                yearFrom: found.yearFrom ?? null,
                yearTo: null,
                minRating: null,
                maxPriceMinor: null,
                language: null,
              },
              similarToTitles: [],
              detectedLocale: "en",
              source: "llm",
              promptVersion: "v1",
              error: null,
            }
          : null,
        page: body.page ?? 1,
        pageSize: 20,
        total: 1,
        degraded: false,
      };
    },
  });
}

async function searchBodies(fetchMock: ReturnType<typeof mockApi>) {
  return Promise.all(
    requestsTo(fetchMock, "/api/search", "POST").map(
      (request) => request.clone().json() as Promise<SearchRequest>,
    ),
  );
}

describe("SearchPage", () => {
  beforeEach(async () => {
    await i18n.changeLanguage("en");
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("explains the rate limit of search", async () => {
    mockApi({
      "GET /api/genres": genres,
      "GET /api/cart": aCart(),
      "POST /api/search": Response.json(
        { status: 429, title: "Too many requests.", code: "rateLimited", retryAfterSeconds: 1 },
        { status: 429, headers: { "Content-Type": "application/problem+json" } },
      ),
    });
    renderApp("/search?q=matrix");

    expect(
      await screen.findByText("Too many searches in a short time. Wait a moment and try again."),
    ).toBeInTheDocument();
  });

  it("asks for a query when there is none", () => {
    stubSearch();
    renderApp("/search");

    expect(screen.getByText(/Type what you want to watch/)).toBeInTheDocument();
  });

  it("searches in hybrid mode with query understanding by default and shows understood chips", async () => {
    const fetchMock = stubSearch({ excludeGenres: ["action"], yearFrom: 2000 });
    renderApp("/search?q=time+travel+without+action&genres=drama");

    expect(await screen.findByText("The Matrix")).toBeInTheDocument();
    const [body] = await searchBodies(fetchMock);
    expect(body).toMatchObject({
      query: "time travel without action",
      mode: "hybrid",
      understand: true,
      debug: false,
      page: 1,
      filters: { genres: ["drama"], excludeGenres: null },
    });

    expect(screen.getByText("Searching for “time travel”.")).toBeInTheDocument();
    const chips = screen.getByRole("list", { name: "Applied filters" });
    const understoodChip = within(chips).getByText("Not Action").closest("li");
    const explicitChip = within(chips).getByText("Drama").closest("li");
    expect(understoodChip).not.toBeNull();
    expect(explicitChip).not.toBeNull();
    expect(
      within(understoodChip as HTMLElement).getByRole("img", { name: "Found in your query" }),
    ).toBeInTheDocument();
    expect(
      within(explicitChip as HTMLElement).queryByRole("img", { name: "Found in your query" }),
    ).not.toBeInTheDocument();
  });

  it("removing an understood chip keeps the other filters and turns understanding off", async () => {
    stubSearch({ excludeGenres: ["action"], yearFrom: 2000 });
    const user = userEvent.setup();
    const { router } = renderApp("/search?q=time+travel&genres=drama");
    await screen.findByText("The Matrix");

    await user.click(screen.getByRole("button", { name: "Remove filter: Not Action" }));

    const params = new URLSearchParams(router.state.location.search);
    expect(params.get("understand")).toBe("0");
    expect(params.get("genres")).toBe("drama");
    expect(params.get("yearFrom")).toBe("2000");
    expect(params.get("excludeGenres")).toBeNull();
    await waitFor(() => {
      expect(screen.queryByText("Not Action")).not.toBeInTheDocument();
    });
  });

  it("switches the mode and shows ranking scores in debug mode", async () => {
    const fetchMock = stubSearch();
    const user = userEvent.setup();
    const { router } = renderApp("/search?q=heist");
    await screen.findByText("The Matrix");

    const modes = screen.getByRole("group", { name: "Search mode" });
    await user.click(within(modes).getByRole("button", { name: "Keyword" }));
    expect(within(modes).getByRole("button", { name: "Keyword" })).toHaveAttribute(
      "aria-pressed",
      "true",
    );
    await user.click(screen.getByRole("checkbox", { name: "Show scores" }));

    expect(router.state.location.search).toBe("?q=heist&mode=keyword&debug=1");
    expect(await screen.findByText("#1 0.6100 (all)")).toBeInTheDocument();
    expect(screen.getByText("#3 0.7100")).toBeInTheDocument();
    const bodies = await searchBodies(fetchMock);
    expect(bodies.at(-1)).toMatchObject({ mode: "keyword", debug: true });
  });
});
