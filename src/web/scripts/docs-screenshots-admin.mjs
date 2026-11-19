// Screenshots of the admin pages (I22) for the administrator handbook: docs/images/screenshots/admin-*.png.
// It signs in as the admin of a running app, queues a stale-only reindex (so the job list has a fresh job) and asks
// the active chat profile one question, so it needs the chat and embedding models of the active profiles.
//
//   BASE_URL=https://localhost:5173 ADMIN_EMAIL=admin@filmsearch.local ADMIN_PASSWORD=… npm run docs:screenshots:admin
//
// ADMIN_PASSWORD is Auth:Admin:Password (API user secrets locally, ADMIN_PASSWORD in deploy/.env). Names limit which
// files are written (e.g. `-- admin-jobs`).
import { mkdirSync } from "node:fs";
import path from "node:path";
import { chromium } from "@playwright/test";

const baseURL = process.env.BASE_URL ?? "https://localhost";
const email = process.env.ADMIN_EMAIL ?? "admin@filmsearch.local";
const password = process.env.ADMIN_PASSWORD;
if (!password) {
    console.error("Set ADMIN_PASSWORD (Auth:Admin:Password of the API).");
    process.exit(1);
}
const outDir = path.resolve(import.meta.dirname, "../../../docs/images/screenshots");
const only = new Set(process.argv.slice(2));

mkdirSync(outDir, { recursive: true });
const browser = await chromium.launch();
const context = await browser.newContext({
    baseURL,
    viewport: { width: 1280, height: 800 },
    locale: "en-US",
    deviceScaleFactor: 1,
    ignoreHTTPSErrors: true,
    colorScheme: "light",
});
const page = await context.newPage();

async function shot(name, options = {}) {
    if (only.size > 0 && !only.has(name)) return;
    await page.waitForLoadState("networkidle").catch(() => undefined);
    await page.waitForTimeout(500);
    await page.screenshot({ path: path.join(outDir, `${name}.png`), ...options });
    console.log(`saved ${name}.png`);
}

await page.goto("/login?returnUrl=%2Fadmin");
await page.getByLabel("Email").fill(email);
await page.getByLabel("Password").fill(password);
await page.getByRole("button", { name: "Sign in" }).click();
await page.getByRole("heading", { name: "Administration", level: 1 }).waitFor();

// Catalog: the seed load is idempotent, so its result is a safe thing to show.
await page.getByRole("button", { name: "Load the seed" }).click();
await page.getByText(/^Genres: \d+\. Movies created/).waitFor({ timeout: 60_000 });
await shot("admin-catalog");

// Search index: queue a stale-only reindex (seconds when nothing changed), then show the state.
await page.goto("/admin/embeddings");
await page.getByRole("button", { name: "Queue the reindex" }).click();
await page.getByText(/The reindex is queued\.|A reindex is already queued or running\./).waitFor();
await page.waitForTimeout(4000);
await page.reload();
await page.getByRole("heading", { name: "Stored vectors" }).waitFor();
await shot("admin-embeddings");

await page.goto("/admin/jobs");
await page.getByRole("table", { name: "Jobs" }).waitFor();
await shot("admin-jobs");

// The newest job in detail: parameters, progress and result as the Worker wrote them.
await page.getByRole("link", { name: "Details" }).first().click();
await page.getByRole("heading", { name: "Parameters" }).waitFor();
// The worker id starts with the machine name: replaced by a neutral one.
await page.locator("dt:text-is('Worker') + dd").evaluate((dd) => {
    dd.textContent = dd.textContent.replace(/^[^/]+/, "worker-host");
});
await shot("admin-job");

await page.goto("/admin/ai");
await page.getByRole("heading", { name: "Chat profiles" }).waitFor();
await page.getByRole("button", { name: "Send" }).click();
await page.getByText(/^Answer of /).waitFor({ timeout: 180_000 });
await shot("admin-ai");

await browser.close();
