import "./index.css";
import "./i18n";
import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { createBrowserRouter } from "react-router";
import { RouterProvider } from "react-router/dom";
import { AppProviders } from "./app/AppProviders";
import { routes } from "./app/routes";

const router = createBrowserRouter(routes);

const root = document.getElementById("root");
if (!root) {
  throw new Error("Element #root is missing in index.html.");
}

createRoot(root).render(
  <StrictMode>
    <AppProviders>
      <RouterProvider router={router} />
    </AppProviders>
  </StrictMode>,
);
