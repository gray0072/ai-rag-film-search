import { defineConfig, devices } from "@playwright/test";

// End-to-end smoke tests against a running deployment (I20): the local Docker Compose stack in the release workflow,
// then the demo environment. Not part of `npm test` (Vitest); run with `npm run e2e`.
//   E2E_BASE_URL        the app URL (default https://localhost, the local stack behind Caddy)
//   E2E_FAKE_PAYMENTS   "0" when the deployment uses Stripe: skips the purchase through the fake payment page
//   E2E_ASSISTANT       "1" to also ask the assistant (calls the chat model: costs tokens)
const baseURL = process.env.E2E_BASE_URL ?? "https://localhost";
const isLocal = new URL(baseURL).hostname === "localhost";

export default defineConfig({
  testDir: "./e2e",
  timeout: 60_000,
  expect: { timeout: 15_000 },
  fullyParallel: true,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 1 : 0,
  reporter: process.env.CI ? [["list"], ["html", { open: "never" }]] : "list",
  use: {
    baseURL,
    // The local stack uses a certificate of Caddy's own CA, which the test browser does not trust.
    ignoreHTTPSErrors: isLocal,
    locale: "en-US",
    trace: "retain-on-failure",
  },
  projects: [{ name: "chromium", use: { ...devices["Desktop Chrome"] } }],
});
