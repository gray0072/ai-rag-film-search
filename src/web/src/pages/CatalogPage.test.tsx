import { screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import i18n from "@/i18n";
import { aCart, aMovie, genres, mockApi, requestsTo } from "@/test/mockApi";
import { renderApp } from "@/test/renderApp";

function stubCatalog() {
  return mockApi({
    "GET /api/genres": genres,
    "GET /api/cart": aCart(),
    "GET /api/info": { name: "FilmSearch", version: "1" },
    "GET /api/movies": (request: Request) => {
      const page = Number(new URL(request.url).searchParams.get("page") ?? "1");
      return {
        items: [aMovie({ title: page === 1 ? "The Matrix" : "Heat" })],
        page,
        pageSize: 20,
        total: 45,
      };
    },
  });
}

function lastMoviesQuery(fetchMock: ReturnType<typeof mockApi>) {
  const requests = requestsTo(fetchMock, "/api/movies");
  return new URL(requests.at(-1)?.url ?? "http://x").searchParams;
}

describe("CatalogPage", () => {
  beforeEach(async () => {
    await i18n.changeLanguage("en");
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("shows movies with the cheapest price and sends the filters from the URL", async () => {
    const fetchMock = stubCatalog();
    renderApp("/?genres=drama&yearFrom=1990&sort=rating");

    const list = await screen.findByRole("list", { name: "Movies" });
    const card = within(list).getByRole("article");
    expect(within(card).getByRole("link", { name: /The Matrix/ })).toHaveAttribute(
      "href",
      `/movies/${aMovie().id}`,
    );
    expect(within(card).getByText("from $3.99")).toBeInTheDocument();
    expect(screen.getByText("Movies: 45")).toBeInTheDocument();

    const query = lastMoviesQuery(fetchMock);
    expect(query.get("genres")).toBe("drama");
    expect(query.get("yearFrom")).toBe("1990");
    expect(query.get("sort")).toBe("rating");
  });

  it("applies filters from the panel to the URL and the request", async () => {
    const fetchMock = stubCatalog();
    const user = userEvent.setup();
    const { router } = renderApp("/");
    const panel = await screen.findByRole("form", { name: "Filters" });

    await user.click(await within(panel).findByRole("checkbox", { name: "Drama" }));
    const year = within(panel).getByRole("group", { name: "Year" });
    await user.type(within(year).getByRole("spinbutton", { name: "From" }), "1990");
    await user.type(within(year).getByRole("spinbutton", { name: "To" }), "2000");
    await user.selectOptions(within(panel).getByRole("combobox", { name: "Rating" }), "8");
    await user.type(within(panel).getByRole("spinbutton", { name: "Max price, USD" }), "4.99");
    await user.click(within(panel).getByRole("button", { name: "Apply" }));

    expect(router.state.location.search).toBe(
      "?genres=drama&yearFrom=1990&yearTo=2000&minRating=8&priceMax=499",
    );
    await waitFor(() => {
      expect(lastMoviesQuery(fetchMock).get("priceMax")).toBe("499");
    });

    // The applied filters show up as chips; removing one updates the URL.
    const chips = screen.getByRole("list", { name: "Applied filters" });
    expect(within(chips).getByText("Drama")).toBeInTheDocument();
    expect(within(chips).getByText("Up to $4.99")).toBeInTheDocument();
    await user.click(within(chips).getByRole("button", { name: "Remove filter: Rating 8+" }));
    expect(router.state.location.search).toBe(
      "?genres=drama&yearFrom=1990&yearTo=2000&priceMax=499",
    );
  });

  it("resets the filters and keeps the sort", async () => {
    stubCatalog();
    const user = userEvent.setup();
    const { router } = renderApp("/?sort=title&genres=drama&minRating=7");

    await user.click(await screen.findByRole("button", { name: "Reset" }));

    expect(router.state.location.search).toBe("?sort=title");
  });

  it("changes the sort and pages through the results", async () => {
    const fetchMock = stubCatalog();
    const user = userEvent.setup();
    const { router } = renderApp("/");
    await screen.findByText("The Matrix");

    await user.selectOptions(screen.getByRole("combobox", { name: "Sort by" }), "year");
    expect(router.state.location.search).toBe("?sort=year");

    const pages = screen.getByRole("navigation", { name: "Pages" });
    expect(within(pages).getByText("Page 1 of 3")).toBeInTheDocument();
    await user.click(within(pages).getByRole("button", { name: "Next" }));

    expect(router.state.location.search).toBe("?sort=year&page=2");
    expect(await screen.findByText("Heat")).toBeInTheDocument();
    expect(lastMoviesQuery(fetchMock).get("page")).toBe("2");
  });
});
