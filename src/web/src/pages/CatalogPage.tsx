import { keepPreviousData } from "@tanstack/react-query";
import { useTranslation } from "react-i18next";
import { useSearchParams } from "react-router";
import { api } from "@/api/client";
import { EmptyState, ErrorState, PageTitle } from "@/components/StatusMessages";
import { FilterChips } from "@/features/filters/FilterChips";
import {
  catalogSorts,
  filterChips,
  parseFilters,
  parsePage,
  parseSort,
  removeChip,
  toListQuery,
  writeFilters,
  type MovieFilters,
} from "@/features/filters/filters";
import { FiltersPanel, SelectField } from "@/features/filters/FiltersPanel";
import { MovieCard } from "@/features/movies/MovieCard";
import { MovieGrid, MovieGridSkeleton } from "@/features/movies/MovieGrid";
import { Pagination } from "@/features/movies/Pagination";

const sortKeys = {
  popularity: "catalog.sort.popularity",
  rating: "catalog.sort.rating",
  year: "catalog.sort.year",
  title: "catalog.sort.title",
} as const;

/** FR-CAT-1/2: paged catalog with filters and sorting; all state lives in the URL. */
export function CatalogPage() {
  const { t } = useTranslation();
  const [searchParams, setSearchParams] = useSearchParams();
  const filters = parseFilters(searchParams);
  const sort = parseSort(searchParams.get("sort"));
  const page = parsePage(searchParams.get("page"));

  const movies = api.useQuery(
    "get",
    "/api/movies",
    { params: { query: toListQuery(filters, sort, page) } },
    // Keep the old page on screen while the next one loads.
    { placeholderData: keepPreviousData },
  );

  const applyFilters = (next: MovieFilters) => {
    setSearchParams(writeFilters(searchParams, next));
  };

  const setSort = (value: string) => {
    const params = new URLSearchParams(searchParams);
    params.delete("page");
    if (value === "popularity") {
      params.delete("sort");
    } else {
      params.set("sort", value);
    }
    setSearchParams(params);
  };

  return (
    <div className="flex flex-col gap-6 md:flex-row md:items-start">
      <aside className="md:sticky md:top-4 md:w-56 md:shrink-0">
        <h2 className="mb-3 font-semibold">{t("filters.title")}</h2>
        <FiltersPanel
          key={writeFilters(new URLSearchParams(), filters).toString()}
          filters={filters}
          onApply={applyFilters}
        />
      </aside>

      <section className="flex min-w-0 flex-1 flex-col gap-4">
        <div className="flex flex-wrap items-end justify-between gap-3">
          <div>
            <PageTitle>{t("catalog.title")}</PageTitle>
            {movies.data && (
              <p className="text-sm text-muted-foreground" aria-live="polite">
                {t("catalog.total", { total: movies.data.total })}
              </p>
            )}
          </div>
          <div className="w-44">
            <SelectField label={t("catalog.sort.label")} value={sort} onChange={setSort}>
              {catalogSorts.map((value) => (
                <option key={value} value={value}>
                  {t(sortKeys[value])}
                </option>
              ))}
            </SelectField>
          </div>
        </div>

        <FilterChips
          chips={filterChips(filters)}
          onRemove={(chip) => {
            applyFilters(removeChip(filters, chip));
          }}
        />

        {movies.isPending ? (
          <MovieGridSkeleton label={t("common.loading")} />
        ) : movies.isError ? (
          <ErrorState onRetry={() => void movies.refetch()} />
        ) : movies.data.items.length === 0 ? (
          <EmptyState>{t("catalog.empty")}</EmptyState>
        ) : (
          <>
            <MovieGrid label={t("catalog.results")} stale={movies.isPlaceholderData}>
              {movies.data.items.map((movie) => (
                <li key={movie.id}>
                  <MovieCard movie={movie} />
                </li>
              ))}
            </MovieGrid>
            <Pagination
              page={movies.data.page}
              pageSize={movies.data.pageSize}
              total={movies.data.total}
            />
          </>
        )}
      </section>
    </div>
  );
}
