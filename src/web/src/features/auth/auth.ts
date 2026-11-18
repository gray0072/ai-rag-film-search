import { useQueryClient } from "@tanstack/react-query";
import { api } from "@/api/client";
import type { CurrentUser } from "@/api/types";

/** Query key of `GET /api/auth/me`. */
export const currentUserQueryKey = api.queryOptions("get", "/api/auth/me").queryKey;

/** The visitor: a signed-in user or `authenticated: false` (I17). */
export function useCurrentUser() {
  return api.useQuery("get", "/api/auth/me", undefined, { staleTime: 5 * 60_000 });
}

/**
 * Signing in or out changes the owner of the cart, orders, library and conversations (spec 04): the new user goes to
 * the cache, every other query is refetched.
 */
function useSetUser() {
  const queryClient = useQueryClient();
  return (user: CurrentUser) => {
    queryClient.setQueryData(currentUserQueryKey, user);
    void queryClient.invalidateQueries({
      predicate: (query) => query.queryKey[1] !== currentUserQueryKey[1],
    });
  };
}

export function useLogin() {
  const setUser = useSetUser();
  return api.useMutation("post", "/api/auth/login", { onSuccess: setUser });
}

export function useRegister() {
  const setUser = useSetUser();
  return api.useMutation("post", "/api/auth/register", { onSuccess: setUser });
}

export function useLogout() {
  const setUser = useSetUser();
  return api.useMutation("post", "/api/auth/logout", {
    onSuccess: () => {
      setUser({ authenticated: false, email: null, roles: [] });
    },
  });
}

/**
 * Where to go after signing in: only a path of this app. `//evil.example` or `https://…` would make the login page an
 * open redirect.
 */
export function safeReturnUrl(value: string | null): string {
  if (!value?.startsWith("/") || value.startsWith("//") || value.startsWith("/\\")) {
    return "/";
  }
  return value;
}

/** The login page that returns to `path` afterwards. */
export function loginUrl(path: string): string {
  return `/login?${new URLSearchParams({ returnUrl: path }).toString()}`;
}
