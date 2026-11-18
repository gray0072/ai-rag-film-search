import { screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import i18n from "@/i18n";
import { aCart, anonymousUser, aUser, mockApi, requestsTo } from "@/test/mockApi";
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
      "GET /api/auth/me": aUser(),
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
    mockApi({ "GET /api/auth/me": aUser(), "GET /api/cart": aCart(), "GET /api/library": [] });
    renderApp("/library");

    expect(await screen.findByText("Du har inga filmer än.")).toBeInTheDocument();
    expect(screen.getByRole("heading", { level: 1, name: "Mitt bibliotek" })).toBeInTheDocument();
  });

  it("sends anonymous visitors to the login page and back to the library after signing in", async () => {
    let user = anonymousUser;
    const fetchMock = mockApi({
      "GET /api/auth/me": () => user,
      "GET /api/cart": aCart(),
      "GET /api/library": [],
      "POST /api/auth/login": () => {
        user = aUser();
        return user;
      },
    });
    const events = userEvent.setup();
    const { router } = renderApp("/library");

    expect(await screen.findByRole("heading", { level: 1, name: "Sign in" })).toBeInTheDocument();
    expect(router.state.location.pathname).toBe("/login");
    expect(router.state.location.search).toBe("?returnUrl=%2Flibrary");
    expect(requestsTo(fetchMock, "/api/library")).toHaveLength(0);

    await events.type(screen.getByLabelText("Email"), "anna@example.com");
    await events.type(screen.getByLabelText("Password"), "correct horse battery");
    await events.click(screen.getByRole("button", { name: "Sign in" }));

    expect(await screen.findByText("You have no movies yet.")).toBeInTheDocument();
    expect(router.state.location.pathname).toBe("/library");
  });
});
