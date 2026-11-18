import createFetchClient, { type Middleware } from "openapi-fetch";
import createQueryHooks from "openapi-react-query";
import { currentLanguage } from "@/i18n";
import type { ApiProblem } from "./problem";
import type { paths } from "./schema";

/** The API returns translated titles and overviews for the UI language (spec 04). */
const acceptLanguage: Middleware = {
  onRequest({ request }) {
    request.headers.set("Accept-Language", currentLanguage());
    return request;
  },
};

/**
 * Every failed call ends with a Problem Details object. An error without a JSON body (a proxy page,
 * an empty 503) would otherwise give an empty string, which TanStack Query treats as success.
 */
const problemDetails: Middleware = {
  async onResponse({ response }) {
    if (response.ok) {
      return undefined;
    }
    const text = await response.clone().text();
    try {
      const body: unknown = JSON.parse(text);
      if (typeof body === "object" && body !== null) {
        return undefined;
      }
    } catch {
      // Not JSON: replaced below.
    }
    const problem: ApiProblem = { status: response.status, title: response.statusText || null };
    return new Response(JSON.stringify(problem), {
      status: response.status,
      statusText: response.statusText,
      headers: { "Content-Type": "application/problem+json" },
    });
  },
};

/** The first API call of the page load; settled when its response headers (and cookie) have arrived. */
let firstCall: Promise<unknown> | undefined;

/**
 * The API gives a browser without the owner cookie (`fs_sid`, spec 04) a new owner on every request. The first page
 * load sends several requests at once (cart, conversation list, …), each would get its own owner, and the last
 * response would win: a conversation or cart line created meanwhile would belong to another owner (404).
 * So the first call goes alone and the others wait for its response, which carries the cookie.
 */
function fetchWithOwner(request: Request): Promise<Response> {
  if (firstCall === undefined) {
    // Look up fetch on every call (not once at import), so tests can stub it.
    const response = globalThis.fetch(request);
    firstCall = response.catch(() => undefined);
    return response;
  }
  return firstCall.then(() => globalThis.fetch(request));
}

/**
 * Typed fetch client generated from the OpenAPI document (`npm run gen:api`).
 * Paths already start with `/api`; in development the Vite dev server proxies them to the API.
 */
export const apiClient = createFetchClient<paths>({
  baseUrl: window.location.origin,
  fetch: fetchWithOwner,
});
apiClient.use(acceptLanguage, problemDetails);

/** TanStack Query hooks over the typed client: `api.useQuery("get", "/api/info")`. */
export const api = createQueryHooks(apiClient);
