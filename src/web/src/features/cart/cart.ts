import { useQueryClient } from "@tanstack/react-query";
import { api } from "@/api/client";
import type { CartView } from "@/api/types";

/** Query key of `GET /api/cart`; cart mutations put their response there. */
export const cartQueryKey = api.queryOptions("get", "/api/cart").queryKey;

export const libraryQueryKey = api.queryOptions("get", "/api/library").queryKey;

/** The current owner's cart (prices from the DB, FR-CAT-5). */
export function useCart() {
  return api.useQuery("get", "/api/cart");
}

/** The cart endpoints answer with the whole cart, so the cache is updated without a refetch. */
function useSetCart() {
  const queryClient = useQueryClient();
  return (cart: CartView) => {
    queryClient.setQueryData(cartQueryKey, cart);
  };
}

export function useAddToCart() {
  const setCart = useSetCart();
  return api.useMutation("post", "/api/cart/items", { onSuccess: setCart });
}

export function useRemoveFromCart() {
  const setCart = useSetCart();
  return api.useMutation("delete", "/api/cart/items/{movieId}", { onSuccess: setCart });
}

/** Active entitlements: the library page and the "already yours" state of a movie page. */
export function useLibrary() {
  return api.useQuery("get", "/api/library");
}
