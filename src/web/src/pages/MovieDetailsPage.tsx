import { Check, ShoppingCart, Star } from "lucide-react";
import { useTranslation } from "react-i18next";
import { Link, useParams } from "react-router";
import { api } from "@/api/client";
import { problemStatus } from "@/api/problem";
import type { MovieDetails, OfferItem } from "@/api/types";
import { ErrorState } from "@/components/StatusMessages";
import { Alert, AlertDescription } from "@/components/ui/alert";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import { useAddToCart, useCart, useLibrary } from "@/features/cart/cart";
import { commerceErrorMessage } from "@/features/cart/commerceErrors";
import { MovieCard } from "@/features/movies/MovieCard";
import { MovieGrid } from "@/features/movies/MovieGrid";
import { sortOffers } from "@/features/movies/offers";
import { Poster } from "@/features/movies/Poster";
import { currentLanguage } from "@/i18n";
import { formatDateTime, formatMoney, formatRating, languageName } from "@/lib/format";
import { NotFoundPage } from "./NotFoundPage";

/** FR-CAT-3: movie details, offers with "add to cart", similar movies. */
export function MovieDetailsPage() {
  const { t } = useTranslation();
  const { id = "" } = useParams();
  const movie = api.useQuery("get", "/api/movies/{id}", { params: { path: { id } } });

  if (movie.isPending) {
    return (
      <div role="status" aria-label={t("common.loading")} className="flex gap-6">
        <Skeleton className="aspect-[2/3] w-48 shrink-0" />
        <div className="flex flex-1 flex-col gap-3">
          <Skeleton className="h-8 w-2/3" />
          <Skeleton className="h-4 w-1/3" />
          <Skeleton className="h-24 w-full" />
        </div>
      </div>
    );
  }

  if (movie.isError) {
    // An unknown id, or a malformed one (400 from the route constraint).
    const status = problemStatus(movie.error);
    return status === 404 || status === 400 ? (
      <NotFoundPage />
    ) : (
      <ErrorState onRetry={() => void movie.refetch()} />
    );
  }

  return (
    <div className="flex flex-col gap-10">
      <MovieInfo movie={movie.data} />
      <SimilarMovies movieId={movie.data.id} />
    </div>
  );
}

function MovieInfo({ movie }: { movie: MovieDetails }) {
  const { t } = useTranslation();
  const language = currentLanguage();
  const showOriginalTitle = movie.originalTitle && movie.originalTitle !== movie.title;

  return (
    <article className="flex flex-col gap-6 sm:flex-row">
      <Poster
        posterPath={movie.posterPath}
        title={movie.title}
        size="w500"
        className="w-48 shrink-0 self-start sm:w-64"
      />
      <div className="flex min-w-0 flex-1 flex-col gap-4">
        <header className="flex flex-col gap-1">
          <h1 className="text-3xl font-semibold tracking-tight">
            {movie.title}
            {movie.year !== null && (
              <span className="ml-2 font-normal text-muted-foreground">({movie.year})</span>
            )}
          </h1>
          {showOriginalTitle && (
            <p className="text-muted-foreground">
              {t("movie.originalTitle", {
                title: movie.originalTitle,
                language: languageName(movie.originalLanguage, language),
              })}
            </p>
          )}
          {movie.tagline && <p className="italic text-muted-foreground">{movie.tagline}</p>}
        </header>

        <div className="flex flex-wrap items-center gap-x-4 gap-y-2 text-sm">
          <span className="inline-flex items-center gap-1">
            <Star className="size-4 fill-current" aria-hidden />
            {t("movie.rating", {
              rating: formatRating(movie.voteAverage, language),
              votes: new Intl.NumberFormat(language).format(movie.voteCount),
            })}
          </span>
          {movie.runtimeMinutes !== null && (
            <span>{t("movie.runtime", { minutes: movie.runtimeMinutes })}</span>
          )}
          <div className="flex flex-wrap gap-1">
            {movie.genres.map((genre) => (
              <Badge key={genre.slug} variant="secondary" asChild>
                <Link to={`/?genres=${encodeURIComponent(genre.slug)}`}>{genre.name}</Link>
              </Badge>
            ))}
          </div>
        </div>

        {movie.overview && <p className="max-w-prose leading-relaxed">{movie.overview}</p>}

        <dl className="grid max-w-prose grid-cols-[auto_1fr] gap-x-4 gap-y-1 text-sm">
          {movie.directors.length > 0 && (
            <>
              <dt className="text-muted-foreground">{t("movie.directors")}</dt>
              <dd>{movie.directors.map((person) => person.name).join(", ")}</dd>
            </>
          )}
          {movie.writers.length > 0 && (
            <>
              <dt className="text-muted-foreground">{t("movie.writers")}</dt>
              <dd>{movie.writers.map((person) => person.name).join(", ")}</dd>
            </>
          )}
          {movie.cast.length > 0 && (
            <>
              <dt className="text-muted-foreground">{t("movie.cast")}</dt>
              <dd>{movie.cast.map((person) => person.name).join(", ")}</dd>
            </>
          )}
        </dl>

        <Offers movieId={movie.id} offers={movie.offers} />
      </div>
    </article>
  );
}

/** Buy / Rent buttons with prices from the DB (FR-CAT-5), the cart and library state of the movie. */
function Offers({ movieId, offers }: { movieId: string; offers: OfferItem[] }) {
  const { t } = useTranslation();
  const language = currentLanguage();
  const cart = useCart();
  const library = useLibrary();
  const addToCart = useAddToCart();

  const inCart = cart.data?.items.find((line) => line.movieId === movieId);
  const entitlement = library.data?.find((item) => item.movieId === movieId);
  const owned = entitlement?.type === "owned";

  return (
    <section aria-labelledby="offers-heading" className="flex flex-col gap-3">
      <h2 id="offers-heading" className="text-lg font-semibold">
        {t("movie.offers")}
      </h2>

      {entitlement && (
        <p className="inline-flex items-center gap-1 text-sm font-medium">
          <Check className="size-4" aria-hidden />
          {owned
            ? t("movie.owned")
            : t("movie.rentedUntil", {
                date: entitlement.validUntil
                  ? formatDateTime(entitlement.validUntil, language)
                  : "",
              })}
        </p>
      )}

      {offers.length === 0 ? (
        <p className="text-sm text-muted-foreground">{t("movie.noOffers")}</p>
      ) : (
        !owned && (
          <div className="flex flex-wrap gap-2">
            {sortOffers(offers).map((offer) => {
              const selected = inCart?.offerType === offer.type;
              const price = formatMoney(offer.priceMinor, offer.currency, language);
              return (
                <Button
                  key={offer.id}
                  variant={selected ? "secondary" : "default"}
                  disabled={
                    addToCart.isPending || selected || (entitlement && offer.type === "rent")
                  }
                  onClick={() => {
                    addToCart.mutate({ body: { movieId, offerType: offer.type } });
                  }}
                >
                  {selected ? <Check aria-hidden /> : <ShoppingCart aria-hidden />}
                  {offer.type === "buy"
                    ? t("offer.buyFor", { price })
                    : t("offer.rentFor", { price, hours: offer.rentalHours ?? 48 })}
                </Button>
              );
            })}
          </div>
        )
      )}

      {inCart && (
        <p className="text-sm">
          {t("movie.inCart")}{" "}
          <Link to="/cart" className="font-medium underline underline-offset-4">
            {t("movie.goToCart")}
          </Link>
        </p>
      )}

      {addToCart.isError && (
        <Alert variant="destructive" className="max-w-prose">
          <AlertDescription>{commerceErrorMessage(t, addToCart.error)}</AlertDescription>
        </Alert>
      )}
    </section>
  );
}

function SimilarMovies({ movieId }: { movieId: string }) {
  const { t } = useTranslation();
  const similar = api.useQuery("get", "/api/movies/{id}/similar", {
    params: { path: { id: movieId }, query: { limit: 10 } },
  });

  // Similar movies need embeddings (I06); without them the section is simply not shown.
  if (!similar.data || similar.data.length === 0) {
    return null;
  }

  return (
    <section aria-labelledby="similar-heading" className="flex flex-col gap-4">
      <h2 id="similar-heading" className="text-lg font-semibold">
        {t("movie.similar")}
      </h2>
      <MovieGrid label={t("movie.similar")}>
        {similar.data.map((item) => (
          <li key={item.movie.id}>
            <MovieCard movie={item.movie} />
          </li>
        ))}
      </MovieGrid>
    </section>
  );
}
