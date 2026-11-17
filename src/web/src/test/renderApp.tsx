import { QueryClient } from "@tanstack/react-query";
import { render } from "@testing-library/react";
import { createMemoryRouter } from "react-router";
import { RouterProvider } from "react-router/dom";
import { AppProviders } from "@/app/AppProviders";
import { routes } from "@/app/routes";

/** Renders the whole app (providers, layout, routes) at the given URL. */
export function renderApp(url = "/") {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });
  const router = createMemoryRouter(routes, { initialEntries: [url] });

  return {
    router,
    ...render(
      <AppProviders queryClient={queryClient}>
        <RouterProvider router={router} />
      </AppProviders>,
    ),
  };
}
