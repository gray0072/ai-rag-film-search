import { Star } from "lucide-react";
import type { ReactNode } from "react";
import { useTranslation } from "react-i18next";
import { Link } from "react-router";
import type { MovieSummary } from "@/api/types";
import { currentLanguage } from "@/i18n";
import { formatMoney, formatRating } from "@/lib/format";
import { cheapestOffer } from "./offers";
import { Poster } from "./Poster";

interface MovieCardProps {
  movie: MovieSummary;
  /** Extra content under the card (search scores in debug mode). */
  children?: ReactNode;
}

export function MovieCard({ movie, children }: MovieCardProps) {
  const { t } = useTranslation();
  const language = currentLanguage();
  const offer = cheapestOffer(movie.offers);

  return (
    <article className="flex flex-col gap-2">
      <Link
        to={`/movies/${movie.id}`}
        className="group flex flex-col gap-2 rounded-md outline-none focus-visible:ring-[3px] focus-visible:ring-ring/50"
      >
        <Poster
          posterPath={movie.posterPath}
          title={movie.title}
          className="transition-opacity group-hover:opacity-90"
        />
        <h3 className="line-clamp-2 font-medium leading-tight group-hover:underline">
          {movie.title}
        </h3>
      </Link>
      <div className="flex flex-wrap items-center gap-x-2 gap-y-1 text-sm text-muted-foreground">
        {movie.year !== null && <span>{movie.year}</span>}
        <span
          className="inline-flex items-center gap-0.5"
          aria-label={t("movie.ratingLabel", { rating: formatRating(movie.voteAverage, language) })}
        >
          <Star className="size-3.5 fill-current" aria-hidden />
          {formatRating(movie.voteAverage, language)}
        </span>
        {offer && (
          <span className="ml-auto font-medium text-foreground">
            {t("offer.priceFrom", {
              price: formatMoney(offer.priceMinor, offer.currency, language),
            })}
          </span>
        )}
      </div>
      {movie.genres.length > 0 && (
        <p className="line-clamp-1 text-xs text-muted-foreground">
          {movie.genres.map((genre) => genre.name).join(" · ")}
        </p>
      )}
      {children}
    </article>
  );
}
