import fs from "node:fs";
import path from "node:path";
import tailwindcss from "@tailwindcss/vite";
import react from "@vitejs/plugin-react";
import { configDefaults, defineConfig } from "vitest/config";

// Aspire injects the API address through service discovery variables (WithReference(api)).
// Without Aspire (plain `npm run dev`), the API launch profile address is used.
const apiUrl =
  process.env.API_HTTPS ??
  process.env.API_HTTP ??
  process.env.services__api__https__0 ??
  process.env.services__api__http__0 ??
  "https://localhost:7180";

// On Windows the working directory may have a lower-case drive letter (`d:\…`: the VS Code terminal,
// a process started by Aspire). Vite canonicalizes its root to `D:\…` but not the alias, so modules
// imported through "@/…" were served from `/@fs/d:/…` with a second copy of React ("Invalid hook call").
// The real path (canonical casing) for both keeps a single module graph.
const webRoot = fs.realpathSync.native(import.meta.dirname);

export default defineConfig({
  root: webRoot,
  plugins: [react(), tailwindcss()],
  resolve: {
    alias: {
      "@": path.join(webRoot, "src"),
    },
  },
  server: {
    // Aspire passes the port in PORT; the default is Vite's.
    port: process.env.PORT ? Number(process.env.PORT) : 5173,
    proxy: {
      // Same-origin calls from the browser: no CORS, and the owner cookie (fs_sid) works as is.
      "/api": {
        target: apiUrl,
        changeOrigin: true,
        // The API uses the ASP.NET Core development certificate, which Node does not trust.
        secure: false,
      },
      // The fake payment page (I09) is served by the API; checkoutUrl is relative to the web app.
      "/fake-checkout": {
        target: apiUrl,
        changeOrigin: true,
        secure: false,
      },
    },
  },
  build: {
    rolldownOptions: {
      output: {
        // Libraries change less often than app code: separate chunks keep them cached between deploys.
        codeSplitting: {
          groups: [
            {
              name: "react",
              test: /node_modules[\\/](react|react-dom|react-router|scheduler)[\\/]/,
            },
            { name: "vendor", test: /node_modules[\\/]/ },
          ],
        },
      },
    },
  },
  test: {
    environment: "jsdom",
    setupFiles: ["./src/test/setup.ts"],
    css: false,
    // Playwright smoke tests of a deployment (npm run e2e), not unit tests.
    exclude: [...configDefaults.exclude, "e2e/**"],
  },
});
