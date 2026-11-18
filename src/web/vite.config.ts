import path from "node:path";
import tailwindcss from "@tailwindcss/vite";
import react from "@vitejs/plugin-react";
import { defineConfig } from "vitest/config";

// Aspire injects the API address through service discovery variables (WithReference(api)).
// Without Aspire (plain `npm run dev`), the API launch profile address is used.
const apiUrl =
  process.env.API_HTTPS ??
  process.env.API_HTTP ??
  process.env.services__api__https__0 ??
  process.env.services__api__http__0 ??
  "https://localhost:7180";

export default defineConfig({
  plugins: [react(), tailwindcss()],
  resolve: {
    alias: {
      "@": path.resolve(import.meta.dirname, "./src"),
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
  },
});
