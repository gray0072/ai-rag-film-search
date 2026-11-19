import { screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { components } from "@/api/schema";
import { jobSummary, parseTmdbIds } from "@/features/admin/admin";
import i18n from "@/i18n";
import { aCart, anonymousUser, aUser, mockApi, requestsTo } from "@/test/mockApi";
import { renderApp } from "@/test/renderApp";

type Schemas = components["schemas"];

const admin = aUser({ email: "admin@filmsearch.local", roles: ["admin"] });

function aJob(overrides: Partial<Schemas["JobStatus"]> = {}): Schemas["JobStatus"] {
  return {
    id: "0190a000-0000-7000-8000-00000000j001",
    kind: "tmdbImport",
    state: "succeeded",
    trigger: "admin",
    parameters: { list: "popular", pages: 5, tmdbIds: null },
    progress: null,
    result: { movies: 100, created: 40, updated: 10, unchanged: 50, failed: 0, errors: [] },
    error: null,
    attempts: 1,
    cancelRequested: false,
    workerId: "worker-1",
    queuedAt: "2026-11-19T10:00:00Z",
    startedAt: "2026-11-19T10:00:01Z",
    heartbeatAt: "2026-11-19T10:00:20Z",
    finishedAt: "2026-11-19T10:00:31Z",
    isActive: false,
    ...overrides,
  };
}

const profiles: Schemas["AdminAiProfiles"] = {
  activeChatProfile: "local",
  fallbackChatProfile: "openrouter",
  chatProfiles: [
    { name: "local", model: "google/gemma-4-e4b", hasApiKey: true, supportsTools: true },
    { name: "openrouter", model: "openai/gpt-6-luna", hasApiKey: false, supportsTools: true },
  ],
  activeEmbeddingProfile: "local",
  embeddingProfiles: [
    {
      name: "local",
      model: "text-embedding-embeddinggemma-300m",
      dimensions: 768,
      hasApiKey: true,
    },
    { name: "nomic", model: "nomic-embed-text-v1.5", dimensions: 768, hasApiKey: true },
  ],
};

const embeddingState: Schemas["EmbeddingIndexState"] = {
  activeProfile: "local",
  activeModel: "text-embedding-embeddinggemma-300m",
  activeDimensions: 768,
  sets: [
    { model: "text-embedding-embeddinggemma-300m", dimensions: 768, locale: "en", count: 1000 },
    { model: "nomic-embed-text-v1.5", dimensions: 768, locale: "en", count: 1000 },
  ],
  job: null,
};

describe("admin pages", () => {
  beforeEach(async () => {
    await i18n.changeLanguage("en");
  });

  afterEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  it("sends visitors to the login page", async () => {
    mockApi({ "GET /api/auth/me": anonymousUser, "GET /api/cart": aCart() });
    const { router } = renderApp("/admin/jobs");

    await waitFor(() => {
      expect(router.state.location.pathname).toBe("/login");
    });
    expect(router.state.location.search).toBe("?returnUrl=%2Fadmin%2Fjobs");
  });

  it("shows no access to users without the admin role and hides the header link", async () => {
    mockApi({ "GET /api/auth/me": aUser(), "GET /api/cart": aCart() });
    renderApp("/admin");

    expect(await screen.findByText("No access")).toBeInTheDocument();
    expect(screen.queryByRole("link", { name: "Admin" })).not.toBeInTheDocument();
  });

  it("loads the seed and queues a TMDB import of exact movies", async () => {
    const fetchMock = mockApi({
      "GET /api/auth/me": admin,
      "GET /api/cart": aCart(),
      "POST /api/admin/ingestion/seed": {
        genres: 19,
        created: 0,
        updated: 2,
        unchanged: 998,
        elapsedMilliseconds: 9400,
      },
      "POST /api/admin/ingestion/tmdb": () =>
        Response.json(aJob({ id: "job-42", state: "queued" }), { status: 202 }),
    });
    const user = userEvent.setup();
    renderApp("/admin");

    const nav = await screen.findByRole("navigation", { name: "Main navigation" });
    expect(await within(nav).findByRole("link", { name: "Admin" })).toHaveAttribute(
      "href",
      "/admin",
    );

    await user.click(screen.getByRole("button", { name: "Load the seed" }));
    expect(
      await screen.findByText(/Movies created: 0, updated: 2, unchanged: 998\. Took 9\.4 s\./),
    ).toBeInTheDocument();

    await user.click(screen.getByRole("radio", { name: "Exact movies" }));
    const ids = screen.getByRole("textbox", { name: "TMDB ids" });
    await user.type(ids, "27205, abc");
    await user.click(screen.getByRole("button", { name: "Queue the import" }));
    expect(screen.getByText(/positive whole numbers/)).toBeInTheDocument();
    expect(requestsTo(fetchMock, "/api/admin/ingestion/tmdb", "POST")).toHaveLength(0);

    await user.clear(ids);
    await user.type(ids, "27205 157336, 27205");
    await user.click(screen.getByRole("button", { name: "Queue the import" }));

    expect(await screen.findByText("The import is queued.")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Open the job" })).toHaveAttribute(
      "href",
      "/admin/jobs/job-42",
    );
    const [request] = requestsTo(fetchMock, "/api/admin/ingestion/tmdb", "POST");
    expect(await request?.json()).toEqual({ tmdbIds: [27205, 157336] });
  });

  it("says when an import is already running", async () => {
    mockApi({
      "GET /api/auth/me": admin,
      "GET /api/cart": aCart(),
      "POST /api/admin/ingestion/tmdb": () =>
        Response.json(aJob({ id: "running-1", state: "running" }), { status: 409 }),
    });
    const user = userEvent.setup();
    renderApp("/admin");

    await user.click(await screen.findByRole("button", { name: "Queue the import" }));

    expect(await screen.findByText("An import is already queued or running.")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Open the job" })).toHaveAttribute(
      "href",
      "/admin/jobs/running-1",
    );
  });

  it("lists jobs with progress and cancels a running one", async () => {
    const running = aJob({
      id: "running-1",
      state: "running",
      progress: { phase: "movies", movies: 100, fetched: 37 },
      result: null,
      finishedAt: null,
      isActive: true,
    });
    const fetchMock = mockApi({
      "GET /api/auth/me": admin,
      "GET /api/cart": aCart(),
      "GET /api/admin/jobs": [
        running,
        aJob({
          id: "reindex-1",
          kind: "embeddingReindex",
          trigger: "startup",
          result: { embedded: 12, unchanged: 988 },
        }),
      ],
      "POST /api/admin/jobs/:id/cancel": { ...running, cancelRequested: true },
    });
    const user = userEvent.setup();
    renderApp("/admin/jobs");

    const table = await screen.findByRole("table", { name: "Jobs" });
    expect(within(table).getByText("Movies: 37 of 100 movies")).toBeInTheDocument();
    expect(within(table).getByText("Embedded 12, unchanged 988")).toBeInTheDocument();
    expect(within(table).getByText("Startup")).toBeInTheDocument();

    await user.click(within(table).getByRole("button", { name: "Cancel" }));

    await waitFor(() => {
      expect(requestsTo(fetchMock, "/api/admin/jobs/running-1/cancel", "POST")).toHaveLength(1);
    });
  });

  it("filters jobs by kind through the URL", async () => {
    const fetchMock = mockApi({
      "GET /api/auth/me": admin,
      "GET /api/cart": aCart(),
      "GET /api/admin/jobs": [],
    });
    const user = userEvent.setup();
    const { router } = renderApp("/admin/jobs");
    expect(await screen.findByText("No jobs yet.")).toBeInTheDocument();

    await user.selectOptions(screen.getByRole("combobox", { name: "Kind" }), "Reindex");

    expect(router.state.location.search).toBe("?kind=embeddingReindex");
    await waitFor(() => {
      const last = requestsTo(fetchMock, "/api/admin/jobs").at(-1);
      expect(last && new URL(last.url).searchParams.get("kind")).toBe("embeddingReindex");
    });
  });

  it("shows a job with its error", async () => {
    mockApi({
      "GET /api/auth/me": admin,
      "GET /api/cart": aCart(),
      "GET /api/admin/jobs/:id": aJob({
        state: "failed",
        result: null,
        error: "TMDB answered 401: invalid API key.",
      }),
    });
    renderApp("/admin/jobs/0190a000-0000-7000-8000-00000000j001");

    expect(await screen.findByText("Error")).toBeInTheDocument();
    expect(screen.getAllByText("TMDB answered 401: invalid API key.").length).toBeGreaterThan(0);
    expect(screen.getByText("Parameters")).toBeInTheDocument();
  });

  it("queues a reindex with a profile and locales and deletes an old model", async () => {
    vi.spyOn(window, "confirm").mockReturnValue(true);
    const fetchMock = mockApi({
      "GET /api/auth/me": admin,
      "GET /api/cart": aCart(),
      "GET /api/admin/embeddings": embeddingState,
      "GET /api/admin/ai/profiles": profiles,
      "POST /api/admin/embeddings/reindex": () =>
        Response.json(
          {
            id: "reindex-2",
            state: "queued",
            trigger: "admin",
            request: { profile: "nomic", onlyStale: false, locales: ["en", "sv"] },
            queuedAt: "2026-11-19T10:00:00Z",
          },
          { status: 202 },
        ),
      "DELETE /api/admin/embeddings": { model: "nomic-embed-text-v1.5", deleted: 1000 },
    });
    const user = userEvent.setup();
    renderApp("/admin/embeddings");

    expect(await screen.findByText("Stored vectors")).toBeInTheDocument();
    expect(
      screen.queryByRole("button", { name: "Delete text-embedding-embeddinggemma-300m" }),
    ).not.toBeInTheDocument();

    await screen.findByRole("option", { name: "nomic" });
    await user.selectOptions(screen.getByRole("combobox", { name: "Embedding profile" }), "nomic");
    await user.click(screen.getByRole("checkbox", { name: "Only movies whose text changed" }));
    await user.click(screen.getByRole("checkbox", { name: "en" }));
    await user.click(screen.getByRole("checkbox", { name: "sv" }));
    await user.click(screen.getByRole("button", { name: "Queue the reindex" }));

    expect(await screen.findByText("The reindex is queued.")).toBeInTheDocument();
    const [reindex] = requestsTo(fetchMock, "/api/admin/embeddings/reindex", "POST");
    expect(await reindex?.json()).toEqual({
      profile: "nomic",
      onlyStale: false,
      locales: ["en", "sv"],
    });

    await user.click(screen.getByRole("button", { name: "Delete nomic-embed-text-v1.5" }));

    expect(
      await screen.findByText("Deleted 1000 vectors of nomic-embed-text-v1.5."),
    ).toBeInTheDocument();
    const [deletion] = requestsTo(fetchMock, "/api/admin/embeddings", "DELETE");
    expect(deletion && new URL(deletion.url).searchParams.get("model")).toBe(
      "nomic-embed-text-v1.5",
    );
  });

  it("lists AI profiles and tries a chat profile", async () => {
    const fetchMock = mockApi({
      "GET /api/auth/me": admin,
      "GET /api/cart": aCart(),
      "GET /api/admin/ai/profiles": profiles,
      "POST /api/admin/ai/chat": {
        profile: "local",
        model: "google/gemma-4-e4b",
        text: "Try Amélie.",
        inputTokens: 21,
        outputTokens: 6,
        estimatedCostUsd: null,
      },
    });
    const user = userEvent.setup();
    renderApp("/admin/ai");

    expect(await screen.findByText("Chat profiles")).toBeInTheDocument();
    expect(screen.getByText("fallback")).toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Send" }));

    expect(await screen.findByText("Try Amélie.")).toBeInTheDocument();
    expect(screen.getByText(/Input: 21 tokens · output: 6 tokens/)).toBeInTheDocument();
    const [request] = requestsTo(fetchMock, "/api/admin/ai/chat", "POST");
    expect(await request?.json()).toEqual({
      profile: "local",
      message: "Recommend one movie for a rainy evening, in one sentence.",
    });
  });

  it("shows why a provider did not answer", async () => {
    mockApi({
      "GET /api/auth/me": admin,
      "GET /api/cart": aCart(),
      "GET /api/admin/ai/profiles": profiles,
      "POST /api/admin/ai/chat": () =>
        Response.json(
          { status: 503, title: "AI provider is not available.", detail: "Connection refused" },
          { status: 503, headers: { "Content-Type": "application/problem+json" } },
        ),
    });
    const user = userEvent.setup();
    renderApp("/admin/ai");

    await user.click(await screen.findByRole("button", { name: "Send" }));

    expect(await screen.findByText("The provider did not answer.")).toBeInTheDocument();
    expect(screen.getByText("Connection refused")).toBeInTheDocument();
  });
});

describe("admin helpers", () => {
  it("parses TMDB ids", () => {
    expect(parseTmdbIds("27205, 157336\n27205")).toEqual([27205, 157336]);
    expect(parseTmdbIds("27205 x")).toBeUndefined();
    expect(parseTmdbIds("0")).toBeUndefined();
    expect(parseTmdbIds("  ")).toBeUndefined();
  });

  it("summarizes the embeddings phase of an import", () => {
    const t = i18n.getFixedT("en");
    expect(
      jobSummary(
        {
          kind: "tmdbImport",
          state: "running",
          progress: { phase: "embeddings", embeddings: { processed: 5, total: 20 } },
          result: null,
          error: null,
        },
        t,
      ),
    ).toBe("Embeddings: 5 of 20 texts");
  });
});
