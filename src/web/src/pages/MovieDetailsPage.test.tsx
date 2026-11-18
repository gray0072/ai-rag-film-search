import { screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { components } from "@/api/schema";
import i18n from "@/i18n";
import { aCart, aMovie, mockApi, problem, requestsTo } from "@/test/mockApi";
import { renderApp } from "@/test/renderApp";

const movie = aMovie();

function details(): components["schemas"]["MovieDetails"] {
  return {
    ...movie,
    tmdbId: 603,
    imdbId: "tt0133093",
    originalLanguage: "en",
    releaseDate: "1999-03-31",
    tagline: "Welcome to the Real World.",
    backdropPath: null,
    keywords: ["simulation"],
    directors: [{ id: "d1", name: "Lana Wachowski" }],
    writers: [],
    cast: [{ id: "c1", name: "Keanu Reeves", character: "Neo" }],
  };
}

describe("MovieDetailsPage", () => {
  beforeEach(async () => {
    await i18n.changeLanguage("en");
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("shows the details and adds the rental to the cart", async () => {
    const fetchMock = mockApi({
      "GET /api/movies/:id": details(),
      "GET /api/movies/:id/similar": [
        { movie: aMovie({ id: "other", title: "Dark City" }), score: 0.9 },
      ],
      "GET /api/cart": aCart(),
      "GET /api/library": [],
      "POST /api/cart/items": aCart([{ movieId: movie.id, offerType: "rent", rentalHours: 48 }]),
    });
    const user = userEvent.setup();
    renderApp(`/movies/${movie.id}`);

    expect(
      await screen.findByRole("heading", { level: 1, name: /The Matrix/ }),
    ).toBeInTheDocument();
    expect(screen.getByText("Welcome to the Real World.")).toBeInTheDocument();
    expect(screen.getByText("Lana Wachowski")).toBeInTheDocument();
    expect(screen.getByRole("img", { name: "Poster of The Matrix" })).toHaveAttribute(
      "src",
      "https://image.tmdb.org/t/p/w500/matrix.jpg",
    );
    const similar = await screen.findByRole("list", { name: "Similar movies" });
    expect(within(similar).getByText("Dark City")).toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Rent for 48 h, $3.99" }));

    const [request] = requestsTo(fetchMock, "/api/cart/items", "POST");
    expect(await request?.clone().json()).toEqual({ movieId: movie.id, offerType: "rent" });
    expect(await screen.findByText("This movie is in your cart.")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Rent for 48 h, $3.99" })).toBeDisabled();
    // The header badge follows the cart without a refetch.
    expect(screen.getByRole("link", { name: "Cart, items: 1" })).toBeInTheDocument();
  });

  it("shows the API's refusal in the UI language", async () => {
    mockApi({
      "GET /api/movies/:id": details(),
      "GET /api/cart": aCart(),
      "GET /api/library": [],
      "POST /api/cart/items": problem(409, "alreadyRented"),
    });
    const user = userEvent.setup();
    renderApp(`/movies/${movie.id}`);

    await user.click(await screen.findByRole("button", { name: "Rent for 48 h, $3.99" }));

    expect(await screen.findByText("You are already renting this movie.")).toBeInTheDocument();
  });

  it("hides the offers of an owned movie", async () => {
    mockApi({
      "GET /api/movies/:id": details(),
      "GET /api/cart": aCart(),
      "GET /api/library": [
        {
          movieId: movie.id,
          title: movie.title,
          year: 1999,
          posterPath: null,
          type: "owned",
          validFrom: "2026-11-18T10:00:00Z",
          validUntil: null,
          orderId: "o1",
        },
      ],
    });
    renderApp(`/movies/${movie.id}`);

    expect(await screen.findByText("This movie is in your library.")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /Buy for/ })).not.toBeInTheDocument();
  });

  it("shows the not found page for an unknown movie", async () => {
    mockApi({ "GET /api/cart": aCart() });
    renderApp("/movies/0190a000-0000-7000-8000-00000000dead");

    expect(
      await screen.findByRole("heading", { level: 1, name: "Page not found" }),
    ).toBeInTheDocument();
  });
});
