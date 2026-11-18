import { screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import i18n from "@/i18n";
import { aCart, anonymousUser, aUser, mockApi, problem, requestsTo } from "@/test/mockApi";
import { renderApp } from "@/test/renderApp";

describe("Login and registration", () => {
  beforeEach(async () => {
    await i18n.changeLanguage("en");
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("signs in, reloads the owner's data and returns to the page it came from", async () => {
    let user = anonymousUser;
    const fetchMock = mockApi({
      "GET /api/auth/me": () => user,
      "GET /api/cart": () => (user.authenticated ? aCart([{ title: "Heat" }]) : aCart()),
      "POST /api/auth/login": () => {
        user = aUser();
        return user;
      },
    });
    const events = userEvent.setup();
    const { router } = renderApp("/login?returnUrl=%2Fcart");

    await events.type(await screen.findByLabelText("Email"), " anna@example.com ");
    await events.type(screen.getByLabelText("Password"), "correct horse battery");
    await events.click(screen.getByRole("button", { name: "Sign in" }));

    expect(await screen.findByRole("heading", { level: 1, name: "Cart" })).toBeInTheDocument();
    expect(router.state.location.pathname).toBe("/cart");
    const [login] = requestsTo(fetchMock, "/api/auth/login", "POST");
    expect(await login?.json()).toEqual({
      email: "anna@example.com",
      password: "correct horse battery",
    });
    // The cart of the account, not the anonymous one from the header.
    expect(await screen.findByText("Heat")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Account: anna@example.com" })).toBeInTheDocument();
  });

  it("shows a refused login in Russian and stays on the page", async () => {
    await i18n.changeLanguage("ru");
    mockApi({
      "GET /api/auth/me": anonymousUser,
      "GET /api/cart": aCart(),
      "POST /api/auth/login": problem(401, "invalidCredentials"),
    });
    const events = userEvent.setup();
    const { router } = renderApp("/login");

    await events.type(await screen.findByLabelText("Эл. почта"), "anna@example.com");
    await events.type(screen.getByLabelText("Пароль"), "wrong password");
    await events.click(screen.getByRole("button", { name: "Войти" }));

    expect(await screen.findByText("Неверная почта или пароль.")).toBeInTheDocument();
    expect(router.state.location.pathname).toBe("/login");
  });

  it("does not follow a return URL to another site", async () => {
    let user = anonymousUser;
    mockApi({
      "GET /api/auth/me": () => user,
      "GET /api/cart": aCart(),
      "GET /api/movies": { items: [], page: 1, pageSize: 20, total: 0 },
      "GET /api/genres": [],
      "POST /api/auth/login": () => {
        user = aUser();
        return user;
      },
    });
    const events = userEvent.setup();
    const { router } = renderApp("/login?returnUrl=%2F%2Fevil.example");

    await events.type(await screen.findByLabelText("Email"), "anna@example.com");
    await events.type(screen.getByLabelText("Password"), "correct horse battery");
    await events.click(screen.getByRole("button", { name: "Sign in" }));

    await vi.waitFor(() => {
      expect(router.state.location.pathname).toBe("/");
    });
  });

  it("explains why an account cannot be created, in Swedish", async () => {
    await i18n.changeLanguage("sv");
    let answer: Response = problem(409, "emailTaken");
    mockApi({
      "GET /api/auth/me": anonymousUser,
      "GET /api/cart": aCart(),
      "POST /api/auth/register": () => answer,
    });
    const events = userEvent.setup();
    renderApp("/register");

    expect(await screen.findByText(/^Minst 8 tecken/)).toBeInTheDocument();
    await events.type(screen.getByLabelText("E-post"), "anna@example.com");
    await events.type(screen.getByLabelText("Lösenord"), "correct horse battery");
    await events.click(screen.getByRole("button", { name: "Skapa konto" }));
    expect(
      await screen.findByText(
        "Det finns redan ett konto med den här e-postadressen. Logga in i stället.",
      ),
    ).toBeInTheDocument();

    answer = Response.json(
      { status: 400, title: "Validation", errors: { Password: ["Too short."] } },
      { status: 400, headers: { "Content-Type": "application/problem+json" } },
    );
    await events.click(screen.getByRole("button", { name: "Skapa konto" }));
    expect(await screen.findByText("Lösenordet måste ha minst 8 tecken.")).toBeInTheDocument();
  });

  it("signs out from the account menu", async () => {
    let user = aUser();
    const fetchMock = mockApi({
      "GET /api/auth/me": () => user,
      "GET /api/cart": aCart(),
      "GET /api/movies": { items: [], page: 1, pageSize: 20, total: 0 },
      "GET /api/genres": [],
      "POST /api/auth/logout": () => {
        user = anonymousUser;
        return new Response(null, { status: 204 });
      },
    });
    const events = userEvent.setup();
    renderApp("/");

    await events.click(await screen.findByRole("button", { name: "Account: anna@example.com" }));
    const menu = await screen.findByRole("menu");
    expect(within(menu).getByText("anna@example.com")).toBeInTheDocument();
    await events.click(within(menu).getByRole("menuitem", { name: "Sign out" }));

    expect(await screen.findByRole("link", { name: "Sign in" })).toBeInTheDocument();
    expect(requestsTo(fetchMock, "/api/auth/logout", "POST")).toHaveLength(1);
  });
});
