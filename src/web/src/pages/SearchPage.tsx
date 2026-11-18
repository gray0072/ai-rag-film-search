import { keepPreviousData } from "@tanstack/react-query";
import { Info } from "lucide-react";
import { useId } from "react";
import { useTranslation } from "react-i18next";
import { useSearchParams } from "react-router";
import { api } from "@/api/client";
import type { SearchItem, SearchMode, SearchRequest, UnderstoodQuery } from "@/api/types";
import { EmptyState, ErrorState, PageTitle } from "@/components/StatusMessages";
import { Alert, AlertDescription } from "@/components/ui/alert";
import { Checkbox } from "@/components/ui/checkbox";
import { Label } from "@/components/ui/label";
import { FilterChips } from "@/features/filters/FilterChips";
import {
  filterChips,
  fromSearchFilters,
  pageSize,
  parseFilters,
  parsePage,
  removeChip,
  sameChip,
  toSearchFilters,
  writeFilters,
  type FilterChip,
  type MovieFilters,
} from "@/features/filters/filters";
import { FiltersPanel } from "@/features/filters/FiltersPanel";
import { MovieCard } from "@/features/movies/MovieCard";
import { MovieGrid, MovieGridSkeleton } from "@/features/movies/MovieGrid";
import { Pagination } from "@/features/movies/Pagination";
import { cn } from "@/lib/utils";

const searchModes = ["hybrid", "keyword", "semantic"] as const satisfies SearchMode[];

const modeKeys = {
  hybrid: "search.mode.hybrid",
  keyword: "search.mode.keyword",
  semantic: "search.mode.semantic",
} as const;

function parseMode(value: string | null): SearchMode {
  return searchModes.find((mode) => mode === value) ?? "hybrid";
}

/**
 * Search results (FR-SRCH-*): mode switch, query understanding with removable chips, catalog filters
 * and a debug view of the ranking scores. State lives in the URL: `q`, `mode`, `understand=0`, `debug=1`,
 * `page` and the filters.
 */
export function SearchPage() {
  const { t } = useTranslation();
  const [searchParams, setSearchParams] = useSearchParams();
  const query = searchParams.get("q")?.trim() ?? "";
  const mode = parseMode(searchParams.get("mode"));
  // Query understanding is on by default: it turns "dramas from the 90s" into filters (I07).
  const understand = searchParams.get("understand") !== "0";
  const debug = searchParams.get("debug") === "1";
  const page = parsePage(searchParams.get("page"));
  const filters = parseFilters(searchParams);

  const body: SearchRequest = {
    query,
    mode,
    filters: toSearchFilters(filters),
    understand,
    page,
    pageSize,
    debug,
  };
  const search = api.useQuery(
    "post",
    "/api/search",
    { body },
    { enabled: query.length > 0, placeholderData: keepPreviousData },
  );

  const setParam = (key: string, value: string | null) => {
    const params = new URLSearchParams(searchParams);
    params.delete("page");
    if (value === null) {
      params.delete(key);
    } else {
      params.set(key, value);
    }
    setSearchParams(params);
  };

  const applyFilters = (next: MovieFilters) => {
    setSearchParams(writeFilters(searchParams, next));
  };

  const explicitChips = filterChips(filters);
  const isUnderstood = (chip: FilterChip) =>
    understand && !explicitChips.some((explicit) => sameChip(explicit, chip));

  // The chips show what the API applied: the request filters merged with the understood ones.
  const appliedFilters = search.data ? fromSearchFilters(search.data.appliedFilters) : filters;

  const onRemoveChip = (chip: FilterChip) => {
    if (!isUnderstood(chip)) {
      applyFilters(removeChip(filters, chip));
      return;
    }
    // The API has no "ignore this understood filter" switch: keep the other applied filters as
    // explicit ones and turn query understanding off, so the removed one is not found again.
    const params = writeFilters(searchParams, removeChip(appliedFilters, chip));
    params.set("understand", "0");
    setSearchParams(params);
  };

  if (!query) {
    return (
      <section className="flex flex-col gap-3">
        <PageTitle>{t("search.title")}</PageTitle>
        <EmptyState>{t("search.enterQuery")}</EmptyState>
      </section>
    );
  }

  return (
    <div className="flex flex-col gap-6 md:flex-row md:items-start">
      <aside className="flex flex-col gap-5 md:sticky md:top-4 md:w-56 md:shrink-0">
        <SearchOptions
          mode={mode}
          understand={understand}
          debug={debug}
          onMode={(value) => {
            setParam("mode", value === "hybrid" ? null : value);
          }}
          onUnderstand={(value) => {
            setParam("understand", value ? null : "0");
          }}
          onDebug={(value) => {
            setParam("debug", value ? "1" : null);
          }}
        />
        <div>
          <h2 className="mb-3 font-semibold">{t("filters.title")}</h2>
          <FiltersPanel
            key={writeFilters(new URLSearchParams(), filters).toString()}
            filters={filters}
            onApply={applyFilters}
          />
        </div>
      </aside>

      <section className="flex min-w-0 flex-1 flex-col gap-4">
        <div>
          <PageTitle>{t("search.resultsFor", { query })}</PageTitle>
          {search.data && (
            <p className="text-sm text-muted-foreground" aria-live="polite">
              {t("search.total", { total: search.data.total })}
            </p>
          )}
        </div>

        {search.data?.understoodQuery && (
          <UnderstoodSummary understood={search.data.understoodQuery} />
        )}

        <FilterChips
          chips={filterChips(appliedFilters)}
          isUnderstood={isUnderstood}
          onRemove={onRemoveChip}
        />

        {search.data?.degraded && (
          <Alert>
            <Info aria-hidden />
            <AlertDescription>{t("search.degraded")}</AlertDescription>
          </Alert>
        )}

        {search.isPending ? (
          <MovieGridSkeleton label={t("search.searching")} />
        ) : search.isError ? (
          <ErrorState
            message={
              mode === "semantic" ? t("search.semanticUnavailable") : t("common.errorGeneric")
            }
            onRetry={() => void search.refetch()}
          />
        ) : search.data.items.length === 0 ? (
          <EmptyState>{t("search.empty")}</EmptyState>
        ) : (
          <>
            <MovieGrid label={t("search.results")} stale={search.isPlaceholderData}>
              {search.data.items.map((item) => (
                <li key={item.movie.id}>
                  <MovieCard movie={item.movie}>{debug && <ScoreDebug item={item} />}</MovieCard>
                </li>
              ))}
            </MovieGrid>
            <Pagination
              page={search.data.page}
              pageSize={search.data.pageSize}
              total={search.data.total}
            />
          </>
        )}
      </section>
    </div>
  );
}

interface SearchOptionsProps {
  mode: SearchMode;
  understand: boolean;
  debug: boolean;
  onMode: (mode: SearchMode) => void;
  onUnderstand: (value: boolean) => void;
  onDebug: (value: boolean) => void;
}

function SearchOptions({
  mode,
  understand,
  debug,
  onMode,
  onUnderstand,
  onDebug,
}: SearchOptionsProps) {
  const { t } = useTranslation();
  const id = useId();

  return (
    <div className="flex flex-col gap-4 text-sm">
      <div role="group" aria-label={t("search.mode.label")} className="flex flex-col gap-2">
        <span className="font-medium" aria-hidden>
          {t("search.mode.label")}
        </span>
        <div className="flex rounded-md border p-0.5">
          {searchModes.map((value) => (
            <button
              key={value}
              type="button"
              aria-pressed={mode === value}
              onClick={() => {
                onMode(value);
              }}
              className={cn(
                "flex-1 rounded-sm px-2 py-1 text-xs outline-none focus-visible:ring-[3px] focus-visible:ring-ring/50",
                mode === value
                  ? "bg-primary text-primary-foreground"
                  : "text-muted-foreground hover:text-foreground",
              )}
            >
              {t(modeKeys[value])}
            </button>
          ))}
        </div>
      </div>
      <div className="flex items-center gap-2">
        <Checkbox
          id={`${id}-understand`}
          checked={understand}
          onCheckedChange={(checked) => {
            onUnderstand(checked === true);
          }}
        />
        <Label htmlFor={`${id}-understand`} className="font-normal">
          {t("search.understand")}
        </Label>
      </div>
      <div className="flex items-center gap-2">
        <Checkbox
          id={`${id}-debug`}
          checked={debug}
          onCheckedChange={(checked) => {
            onDebug(checked === true);
          }}
        />
        <Label htmlFor={`${id}-debug`} className="font-normal">
          {t("search.debug.toggle")}
        </Label>
      </div>
    </div>
  );
}

/** What query understanding made of the text: the semantic part and similar titles. */
function UnderstoodSummary({ understood }: { understood: UnderstoodQuery }) {
  const { t } = useTranslation();

  if (understood.source === "fallback") {
    return <p className="text-sm text-muted-foreground">{t("search.understoodFallback")}</p>;
  }

  const similar = understood.similarToTitles ?? [];
  return (
    <p className="text-sm text-muted-foreground">
      {t("search.understoodAs", { query: understood.semanticQuery })}
      {similar.length > 0 && ` ${t("search.similarTo", { titles: similar.join(", ") })}`}
    </p>
  );
}

function ScoreDebug({ item }: { item: SearchItem }) {
  const { t } = useTranslation();
  const debug = item.debug;
  const number = (value: number | null | undefined, digits = 4) =>
    value === null || value === undefined ? "–" : value.toFixed(digits);
  const rank = (value: number | null | undefined) =>
    value === null || value === undefined ? "–" : `#${value}`;

  return (
    <dl
      aria-label={t("search.debug.label")}
      className="grid grid-cols-[auto_1fr] gap-x-2 rounded-md bg-muted p-2 font-mono text-[11px] leading-snug text-muted-foreground"
    >
      <dt>{t("search.debug.score")}</dt>
      <dd>{number(item.score)}</dd>
      {debug && (
        <>
          <dt>{t("search.debug.keyword")}</dt>
          <dd>
            {rank(debug.keywordRank)} {number(debug.keywordScore)}
            {debug.keywordMatch ? ` (${debug.keywordMatch})` : ""}
          </dd>
          <dt>{t("search.debug.vector")}</dt>
          <dd>
            {rank(debug.vectorRank)} {number(debug.similarity)}
          </dd>
          <dt>{t("search.debug.fused")}</dt>
          <dd>{number(debug.fusedScore)}</dd>
        </>
      )}
    </dl>
  );
}
