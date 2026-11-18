import { screen, within } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import i18n from "@/i18n";
import { aCart, mockApi } from "@/test/mockApi";
import { renderApp } from "@/test/renderApp";

describe("LibraryPage", () => {
  beforeEach(async () => {
    await i18n.changeLanguage("en");
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("lists owned movies and rentals with their end", async () => {
    mockApi({
      "GET /api/cart": aCart(),
      "GET /api/library": [
        {
          movieId: "m1",
          title: "Heat",
          year: 1995,
          posterPath: null,
          type: "rental",
          validFrom: "2026-11-18T10:00:00Z",
          validUntil: "2026-11-20T10:00:00Z",
          orderId: "o1",
        },
        {
          movieId: "m2",
          title: "The Matrix",
          year: 1999,
          posterPath: null,
          type: "owned",
          validFrom: "2026-11-17T10:00:00Z",
          validUntil: null,
          orderId: "o2",
        },
      ],
    });
    renderApp("/library");

    const list = await screen.findByRole("list", { name: "Your movies" });
    const items = within(list).getAllByRole("listitem");
    expect(items).toHaveLength(2);
    const [rental, owned] = items as [HTMLElement, HTMLElement];
    expect(within(rental).getByText("Rental")).toBeInTheDocument();
    expect(within(rental).getByText(/^Until Nov 20, 2026/)).toBeInTheDocument();
    expect(within(owned).getByText("Owned")).toBeInTheDocument();
  });

  it("shows the empty library in Swedish", async () => {
    await i18n.changeLanguage("sv");
    mockApi({ "GET /api/cart": aCart(), "GET /api/library": [] });
    renderApp("/library");

    expect(await screen.findByText("Du har inga filmer än.")).toBeInTheDocument();
    expect(screen.getByRole("heading", { level: 1, name: "Mitt bibliotek" })).toBeInTheDocument();
  });
});
