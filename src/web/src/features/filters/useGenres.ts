import { api } from "@/api/client";

/** Genres change rarely; the names follow the UI language (refetched on a language switch). */
export function useGenres() {
  return api.useQuery("get", "/api/genres", undefined, { staleTime: 10 * 60_000 });
}
