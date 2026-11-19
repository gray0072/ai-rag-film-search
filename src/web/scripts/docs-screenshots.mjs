// Screenshots for the documentation (I21): README, handbook and guide use the PNG files this script writes to
// docs/images/screenshots. It drives a running app (the AppHost's web URL or the local Docker stack) with a chat model
// and an embedding model available, and buys a movie through the fake payment page.
//
//   BASE_URL=http://localhost:5173 npm run docs:screenshots [-- name…]
//
// BASE_URL is the web URL from the Aspire dashboard (default https://localhost, the Docker stack). With
// ASPIRE_LOGIN_URL (printed by the AppHost) it also captures the trace of an assistant turn. Names limit which files
// are written (e.g. `-- catalog search`); the whole flow still runs. Every run registers a new user.
import { mkdirSync } from "node:fs";
import path from "node:path";
import { chromium } from "@playwright/test";

const baseURL = process.env.BASE_URL ?? "https://localhost";
const outDir = path.resolve(import.meta.dirname, "../../../docs/images/screenshots");
const only = new Set(process.argv.slice(2));
const viewport = { width: 1280, height: 800 };

mkdirSync(outDir, { recursive: true });
const browser = await chromium.launch();

async function newPage(locale) {
    const context = await browser.newContext({
        baseURL,
        viewport,
        locale,
        deviceScaleFactor: 1,
        ignoreHTTPSErrors: true,
        colorScheme: "light",
    });
    return context.newPage();
}

async function shot(page, name, options = {}) {
    if (only.size > 0 && !only.has(name)) return;
    // Images load lazily from TMDB; give them a moment after the content is there.
    await page.waitForLoadState("networkidle").catch(() => undefined);
    await page.waitForTimeout(800);
    await page.screenshot({ path: path.join(outDir, `${name}.png`), ...options });
    console.log(`saved ${name}.png`);
}

const page = await newPage("en-US");

// An account first: the library needs one, and the header then shows the user menu.
await page.goto("/register");
await page.getByLabel("Email").fill(`docs-${Date.now()}@example.com`);
await page.getByLabel("Password").fill("a long and memorable passphrase");
await page.getByRole("button", { name: "Create account" }).click();
await page.waitForURL((url) => !url.pathname.startsWith("/register"));

await page.goto("/");
await page.getByRole("article").first().waitFor();
await shot(page, "catalog");

await page.goto(`/search?q=${encodeURIComponent("a heist inside dreams")}`);
await page.getByText(/^Movies found: [1-9]/).waitFor({ timeout: 60_000 });
await shot(page, "search");
// Ranking scores: the keyword rank, the vector rank and the fused RRF score of every result.
await page.goto(`/search?q=${encodeURIComponent("a heist inside dreams")}&debug=1`);
await page.getByLabel("Ranking scores").first().waitFor({ timeout: 60_000 });
await shot(page, "search-scores");

await page.getByRole("article").first().getByRole("link").first().click();
await page.getByRole("heading", { level: 1 }).waitFor();
await shot(page, "movie");

// The assistant: a grounded answer with movie cards, then a cart and an order summary with a confirmation button.
await page.goto("/chat");
await page
    .getByRole("textbox", { name: "Message to the assistant" })
    .fill("Want something for an evening with my wife, we liked Before Sunrise and La La Land.");
await page.getByRole("button", { name: "Send" }).click();
const firstAnswer = page.getByRole("listitem", { name: "Assistant" }).last();
await firstAnswer
    .getByRole("group", { name: "Movies from the catalog" })
    .waitFor({ timeout: 180_000 });
await page.getByRole("button", { name: "Good answer" }).last().waitFor({ timeout: 180_000 });
await shot(page, "assistant");

await page
    .getByRole("textbox", { name: "Message to the assistant" })
    .fill("Buy the first one for me, I want to pay now.");
await page.getByRole("button", { name: "Send" }).click();
const checkout = page.getByRole("button", { name: /confirm|pay/i }).last();
await checkout.waitFor({ timeout: 180_000 }).then(
    () => shot(page, "assistant-checkout"),
    () => console.log("skipped assistant-checkout: the model did not prepare an order"),
);

await page.goto("/cart");
await page.getByRole("heading", { name: "Cart", level: 1 }).waitFor();
const cartHasItems = await page.getByRole("button", { name: "Go to payment" }).isVisible();
if (!cartHasItems) {
    // The model did not add anything: buy the movie of the movie shot directly.
    const list = await page.request.get("/api/movies?offerType=buy&pageSize=1");
    const { items } = await list.json();
    await page.goto(`/movies/${items[0].id}`);
    await page.getByRole("button", { name: /^Buy for / }).click();
    await page.getByText("This movie is in your cart.").waitFor();
    await page.goto("/cart");
}
await shot(page, "cart");

await page.getByRole("button", { name: "Go to payment" }).click();
await page.getByRole("button", { name: "Pay", exact: true }).waitFor();
await shot(page, "fake-payment");
await page.getByRole("button", { name: "Pay", exact: true }).click();
await page.getByRole("heading", { name: "Thank you!" }).waitFor({ timeout: 30_000 });
await shot(page, "checkout-success");

await page.goto("/library");
await page.getByRole("heading", { name: "My library", level: 1 }).waitFor();
await shot(page, "library");

// Multilingual search: a Russian query with a negation, in the Russian UI.
const ru = await newPage("ru-RU");
await ru.goto(
    `/search?q=${encodeURIComponent("фильм про путешествие во времени, но без боевиков")}`,
);
await ru.getByRole("article").first().waitFor({ timeout: 60_000 });
await shot(ru, "search-ru");

const sv = await newPage("sv-SE");
await sv.goto("/chat");
await sv.getByRole("textbox").last().fill("Jag vill se en mysig film om Stockholm, gärna svensk.");
await sv.keyboard.press("Enter");
await sv
    .getByRole("group")
    .last()
    .waitFor({ timeout: 180_000 })
    .catch(() => undefined);
await sv.waitForTimeout(3000);
await shot(sv, "assistant-sv");

// Optional: the trace of an assistant turn in the Aspire dashboard (the login URL is printed by the AppHost).
const aspireLogin = process.env.ASPIRE_LOGIN_URL;
if (aspireLogin) {
    const aspire = await (
        await browser.newContext({
            viewport: { width: 1440, height: 900 },
            ignoreHTTPSErrors: true,
        })
    ).newPage();
    await aspire.goto(aspireLogin);
    await aspire.waitForTimeout(2000);
    await aspire.goto(new URL("/traces", aspireLogin).href);
    await aspire.locator("input#control[name=traces-search]").fill("messages");
    await aspire.waitForTimeout(2000);
    await aspire.locator("text=/POST .*messages/").first().click();
    await aspire.getByText("invoke_agent assistant", { exact: true }).waitFor();
    await shot(aspire, "aspire-trace");
    // The GenAI view of the first model call: messages, tool calls and token usage.
    await aspire
        .getByText(/^chat /)
        .first()
        .click();
    await aspire.waitForTimeout(1500);
    await shot(aspire, "aspire-span");
}

await browser.close();
