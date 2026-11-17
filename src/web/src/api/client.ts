import createFetchClient, { type Middleware } from "openapi-fetch";
import createQueryHooks from "openapi-react-query";
import { currentLanguage } from "@/i18n";
import type { paths } from "./schema";

/** The API returns translated titles and overviews for the UI language (spec 04). */
const acceptLanguage: Middleware = {
  onRequest({ request }) {
    request.headers.set("Accept-Language", currentLanguage());
    return request;
  },
};

/**
 * Typed fetch client generated from the OpenAPI document (`npm run gen:api`).
 * Paths already start with `/api`; in development the Vite dev server proxies them to the API.
 */
export const apiClient = createFetchClient<paths>({
  baseUrl: window.location.origin,
  // Look up fetch on every call (not once at import), so tests can stub it.
  fetch: (request) => globalThis.fetch(request),
});
apiClient.use(acceptLanguage);

/** TanStack Query hooks over the typed client: `api.useQuery("get", "/api/info")`. */
export const api = createQueryHooks(apiClient);
