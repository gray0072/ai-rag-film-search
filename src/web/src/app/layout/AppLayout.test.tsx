import { screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import i18n from "@/i18n";
import { languageStorageKey } from "@/i18n/languages";
import { renderApp } from "@/test/renderApp";

function stubApiInfo(version = "1.2.3") {
  const fetchMock = vi.fn((request: Request) => {
    if (new URL(request.url).pathname === "/api/info") {
      return Promise.resolve(Response.json({ name: "FilmSearch", version }));
    }
    return Promise.resolve(new Response(null, { status: 404 }));
  });
  vi.stubGlobal("fetch", fetchMock);
  return fetchMock;
}

describe("AppLayout", () => {
  beforeEach(async () => {
    localStorage.clear();
    await i18n.changeLanguage("en");
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("renders the header, the page and the API version from the generated client", async () => {
    stubApiInfo("1.2.3");
    renderApp("/");

    const nav = screen.getByRole("navigation", { name: "Main navigation" });
    expect(within(nav).getByRole("link", { name: "Catalog" })).toBeInTheDocument();
    expect(screen.getByRole("heading", { level: 1, name: "Catalog" })).toBeInTheDocument();
    expect(screen.getByRole("searchbox", { name: "Search movies" })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Cart" })).toHaveAttribute("href", "/cart");

    expect(await screen.findByText("API 1.2.3")).toBeInTheDocument();
  });

  it("switches all visible texts, stores the language and sends it as Accept-Language", async () => {
    const fetchMock = stubApiInfo();
    const user = userEvent.setup();
    renderApp("/");
    await screen.findByText("API 1.2.3");

    await user.selectOptions(screen.getByRole("combobox", { name: "Language" }), "ru");

    expect(screen.getByRole("heading", { level: 1, name: "Каталог" })).toBeInTheDocument();
    expect(screen.getByRole("searchbox", { name: "Поиск фильмов" })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Корзина" })).toBeInTheDocument();
    expect(screen.getByRole("combobox", { name: "Язык" })).toHaveValue("ru");
    expect(document.documentElement.lang).toBe("ru");
    expect(localStorage.getItem(languageStorageKey)).toBe("ru");

    // Server data is refetched in the new language.
    await waitFor(() => {
      const languages = fetchMock.mock.calls
        .map(([request]) => request)
        .filter((request) => new URL(request.url).pathname === "/api/info")
        .map((request) => request.headers.get("Accept-Language"));
      expect(languages).toEqual(["en", "ru"]);
    });
  });

  it("shows that the API is unavailable when the call fails", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn(() => Promise.resolve(new Response(null, { status: 503 }))),
    );
    renderApp("/");

    expect(await screen.findByText("API unavailable")).toBeInTheDocument();
  });

  it("submits the search box to the search page", async () => {
    stubApiInfo();
    const user = userEvent.setup();
    const { router } = renderApp("/");

    await user.type(screen.getByRole("searchbox", { name: "Search movies" }), "space opera{Enter}");

    expect(router.state.location.pathname).toBe("/search");
    expect(router.state.location.search).toBe("?q=space+opera");
    expect(
      screen.getByRole("heading", { level: 1, name: "Results for “space opera”" }),
    ).toBeInTheDocument();
  });

  it("shows the not found page for an unknown route", () => {
    stubApiInfo();
    renderApp("/no-such-page");

    expect(screen.getByRole("heading", { level: 1, name: "Page not found" })).toBeInTheDocument();
  });
});
