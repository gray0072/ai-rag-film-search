import { vi } from "vitest";
import type { components } from "@/api/schema";

type Schemas = components["schemas"];

/** A route answer: a JSON body (200), a Response, or a function `(request, pathParams) => answer`. */
export type RouteAnswer = unknown;

interface Route {
  method: string;
  pattern: RegExp;
  names: string[];
  answer: RouteAnswer;
}

function compile(key: string, answer: RouteAnswer): Route {
  const [method = "GET", path = "/"] = key.split(" ");
  const names: string[] = [];
  const pattern = new RegExp(
    `^${path.replace(/:(\w+)/g, (_, name: string) => {
      names.push(name);
      return "([^/]+)";
    })}$`,
  );
  return { method, pattern, names, answer };
}

/**
 * Stubs `fetch` with an in-memory API: `{ "GET /api/movies/:id": movie, "POST /api/cart/items": fn }`.
 * Unknown routes answer 404 Problem Details. Returns the mock, so tests can read the requests.
 */
export function mockApi(routes: Record<string, RouteAnswer>) {
  const compiled = Object.entries(routes).map(([key, answer]) => compile(key, answer));
  const fetchMock = vi.fn(async (request: Request) => {
    const url = new URL(request.url);
    for (const route of compiled) {
      const match = route.method === request.method ? route.pattern.exec(url.pathname) : null;
      if (!match) {
        continue;
      }
      const params = Object.fromEntries(
        route.names.map((name, index) => [name, decodeURIComponent(match[index + 1] ?? "")]),
      );
      const result: unknown =
        typeof route.answer === "function"
          ? await (route.answer as (r: Request, p: Record<string, string>) => unknown)(
              // A clone: the test can still read the body of the recorded request.
              request.clone(),
              params,
            )
          : route.answer;
      return result instanceof Response ? result.clone() : Response.json(result);
    }
    return problem(404);
  });
  vi.stubGlobal("fetch", fetchMock);
  return fetchMock;
}

/** A Problem Details response, with the commerce `code` extension when given. */
export function problem(status: number, code?: string): Response {
  return Response.json(
    { status, title: "Error", ...(code ? { code } : {}) },
    { status, headers: { "Content-Type": "application/problem+json" } },
  );
}

/** The requests the mock received for a path (and method). */
export function requestsTo(fetchMock: ReturnType<typeof mockApi>, path: string, method = "GET") {
  return fetchMock.mock.calls
    .map(([request]) => request)
    .filter((request) => request.method === method && new URL(request.url).pathname === path);
}

export function aMovie(overrides: Partial<Schemas["MovieSummary"]> = {}): Schemas["MovieSummary"] {
  return {
    id: "0190a000-0000-7000-8000-000000000001",
    title: "The Matrix",
    originalTitle: "The Matrix",
    year: 1999,
    runtimeMinutes: 136,
    overview: "A hacker learns the truth about reality.",
    posterPath: "/matrix.jpg",
    voteAverage: 8.2,
    voteCount: 25000,
    genres: [{ id: 878, slug: "science-fiction", name: "Science Fiction" }],
    offers: [
      {
        id: "0190a000-0000-7000-8000-0000000000b1",
        type: "buy",
        priceMinor: 999,
        currency: "USD",
        rentalHours: null,
      },
      {
        id: "0190a000-0000-7000-8000-0000000000r1",
        type: "rent",
        priceMinor: 399,
        currency: "USD",
        rentalHours: 48,
      },
    ],
    ...overrides,
  };
}

export const genres: Schemas["GenreItem"][] = [
  { id: 28, slug: "action", name: "Action" },
  { id: 18, slug: "drama", name: "Drama" },
  { id: 878, slug: "science-fiction", name: "Science Fiction" },
];

export function aCart(lines: Partial<Schemas["CartLine"]>[] = []): Schemas["CartView"] {
  const items = lines.map((line, index): Schemas["CartLine"] => ({
    movieId: `0190a000-0000-7000-8000-00000000000${String(index + 1)}`,
    title: `Movie ${String(index + 1)}`,
    year: 2000,
    posterPath: null,
    offerId: `0190a000-0000-7000-8000-0000000000f${String(index + 1)}`,
    offerType: "buy",
    priceMinor: 999,
    currency: "USD",
    rentalHours: null,
    addedBy: "user",
    addedAt: "2026-11-18T10:00:00Z",
    available: true,
    ...line,
  }));
  return {
    items,
    totalMinor: items
      .filter((line) => line.available)
      .reduce((sum, line) => sum + line.priceMinor, 0),
    currency: "USD",
  };
}

export function anOrder(overrides: Partial<Schemas["OrderView"]> = {}): Schemas["OrderView"] {
  return {
    id: "0190a000-0000-7000-8000-0000000000aa",
    status: "pendingPayment",
    totalMinor: 999,
    currency: "USD",
    createdAt: "2026-11-18T10:00:00Z",
    expiresAt: "2026-11-18T10:30:00Z",
    paidAt: null,
    source: "web",
    items: [
      {
        movieId: "0190a000-0000-7000-8000-000000000001",
        offerId: "0190a000-0000-7000-8000-0000000000f1",
        offerType: "buy",
        title: "The Matrix",
        priceMinor: 999,
        rentalHours: null,
      },
    ],
    ...overrides,
  };
}

/** A signed-in user (`GET /api/auth/me`, I17). */
export function aUser(overrides: Partial<Schemas["CurrentUser"]> = {}): Schemas["CurrentUser"] {
  return { authenticated: true, email: "anna@example.com", roles: [], ...overrides };
}

/** `GET /api/auth/me` of a visitor who is not signed in. */
export const anonymousUser: Schemas["CurrentUser"] = {
  authenticated: false,
  email: null,
  roles: [],
};
