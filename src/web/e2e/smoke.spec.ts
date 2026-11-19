import { expect, test } from "@playwright/test";

// Smoke tests of a deployment (I20): every test uses a fresh browser context, so a new anonymous owner.

test("the API is up behind the proxy", async ({ request }) => {
  const health = await request.get("/health");
  expect(health.ok()).toBe(true);
  // Degraded: the store works but an AI provider is not reachable (the runner has no OpenRouter key).
  expect(["Healthy", "Degraded"]).toContain(await health.text());
});

test("the catalog shows movies and a movie opens", async ({ page }) => {
  await page.goto("/");
  await expect(page.getByRole("heading", { name: "Catalog", level: 1 })).toBeVisible();

  const firstMovie = page.getByRole("article").first().getByRole("link");
  const title = (await firstMovie.innerText()).trim();
  await firstMovie.click();

  await expect(page).toHaveURL(/\/movies\/[0-9a-f-]{36}$/);
  await expect(page.getByRole("heading", { name: title, level: 1 })).toBeVisible();
});

test("a client-side route loads directly", async ({ page }) => {
  await page.goto("/cart");
  await expect(page.getByRole("heading", { name: "Cart", level: 1 })).toBeVisible();
});

test("search finds movies", async ({ page }) => {
  await page.goto("/");
  const box = page.getByRole("searchbox", { name: "Search movies" });
  await box.fill("space adventure");
  await box.press("Enter");

  await expect(page).toHaveURL(/\/search\?/);
  await expect(page.getByText(/^Movies found: [1-9]/)).toBeVisible();
  await expect(page.getByRole("article").first()).toBeVisible();
});

test("a movie can be bought through the fake payment page", async ({ page }) => {
  test.skip(process.env.E2E_FAKE_PAYMENTS === "0", "The deployment uses Stripe.");

  const list = await page.request.get("/api/movies?offerType=buy&pageSize=1");
  expect(list.ok()).toBe(true);
  const movies = (await list.json()) as { items: { id: string; title: string }[] };
  const movie = movies.items[0];
  expect(movie).toBeDefined();
  if (!movie) return;

  await page.goto(`/movies/${movie.id}`);
  await page.getByRole("button", { name: /^Buy for / }).click();
  await expect(page.getByText("This movie is in your cart.")).toBeVisible();

  await page.goto("/cart");
  await page.getByRole("button", { name: "Go to payment" }).click();

  // The fake provider's hosted page (served by the API) sends the browser back to the shop after "Pay"; the success
  // page then polls the order until the (delayed, signed) webhook marks it paid.
  await page.getByRole("button", { name: "Pay", exact: true }).click();

  await expect(page).toHaveURL(/\/checkout\/success\?orderId=/);
  await expect(page.getByRole("heading", { name: "Thank you!" })).toBeVisible({ timeout: 30_000 });
});

test("the assistant answers", async ({ page }) => {
  test.skip(process.env.E2E_ASSISTANT !== "1", "Set E2E_ASSISTANT=1 to call the chat model.");

  await page.goto("/chat");
  await page
    .getByRole("textbox", { name: "Message to the assistant" })
    .fill("Recommend one science fiction movie.");
  await page.getByRole("button", { name: "Send" }).click();

  // A grounded answer: text plus movie cards from the catalog (search_movies ran).
  const answer = page.getByRole("listitem", { name: "Assistant" }).last();
  await expect(answer.getByRole("group", { name: "Movies from the catalog" })).toBeVisible({
    timeout: 90_000,
  });
});
