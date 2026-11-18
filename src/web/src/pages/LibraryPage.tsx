import { useTranslation } from "react-i18next";
import { Link } from "react-router";
import type { LibraryItem } from "@/api/types";
import { EmptyState, ErrorState, PageTitle } from "@/components/StatusMessages";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { useLibrary } from "@/features/cart/cart";
import { MovieGridSkeleton } from "@/features/movies/MovieGrid";
import { Poster } from "@/features/movies/Poster";
import { currentLanguage } from "@/i18n";
import { formatDateTime } from "@/lib/format";

/** FR-COM-6: movies the owner may watch; rentals show when they end. */
export function LibraryPage() {
  const { t } = useTranslation();
  const library = useLibrary();

  return (
    <section className="flex flex-col gap-6">
      <PageTitle>{t("library.title")}</PageTitle>
      {library.isPending ? (
        <MovieGridSkeleton count={5} label={t("common.loading")} />
      ) : library.isError ? (
        <ErrorState onRetry={() => void library.refetch()} />
      ) : library.data.length === 0 ? (
        <EmptyState>
          <p>{t("library.empty")}</p>
          <Button asChild variant="link">
            <Link to="/">{t("cart.browse")}</Link>
          </Button>
        </EmptyState>
      ) : (
        <ul
          aria-label={t("library.items")}
          className="grid grid-cols-2 gap-x-4 gap-y-6 sm:grid-cols-3 lg:grid-cols-4 xl:grid-cols-5"
        >
          {library.data.map((item) => (
            <LibraryCard key={item.movieId} item={item} />
          ))}
        </ul>
      )}
    </section>
  );
}

function LibraryCard({ item }: { item: LibraryItem }) {
  const { t } = useTranslation();
  const language = currentLanguage();

  return (
    <li className="flex flex-col gap-2">
      <Link to={`/movies/${item.movieId}`} className="group flex flex-col gap-2">
        <Poster posterPath={item.posterPath} title={item.title} />
        <h2 className="line-clamp-2 font-medium leading-tight group-hover:underline">
          {item.title}
        </h2>
      </Link>
      <div className="flex flex-wrap items-center gap-2 text-sm text-muted-foreground">
        {item.year !== null && <span>{item.year}</span>}
        <Badge variant={item.type === "owned" ? "default" : "secondary"}>
          {item.type === "owned" ? t("library.owned") : t("library.rental")}
        </Badge>
      </div>
      {item.type === "rental" && item.validUntil && (
        <p className="text-xs text-muted-foreground">
          {t("library.until", { date: formatDateTime(item.validUntil, language) })}
        </p>
      )}
    </li>
  );
}
