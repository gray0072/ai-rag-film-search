import { Check, ShoppingCart, Star } from "lucide-react";
import { useTranslation } from "react-i18next";
import { Link } from "react-router";
import type { AssistantMovie, OfferItem } from "@/api/types";
import { Button } from "@/components/ui/button";
import { useAddToCart, useCart } from "@/features/cart/cart";
import { commerceErrorMessage } from "@/features/cart/commerceErrors";
import { sortOffers } from "@/features/movies/offers";
import { Poster } from "@/features/movies/Poster";
import { currentLanguage } from "@/i18n";
import { formatMoney, formatRating } from "@/lib/format";

/**
 * A movie the assistant found (FR-AST-3): catalog data and DB prices from the event or the history,
 * never from the model's text. The offer buttons add the movie to the cart like the movie page does.
 */
export function ChatMovieCard({ movie }: { movie: AssistantMovie }) {
  const { t } = useTranslation();
  const language = currentLanguage();
  const cart = useCart();
  const add = useAddToCart();
  const inCart = cart.data?.items.some((line) => line.movieId === movie.id) ?? false;

  const offerLabel = (offer: OfferItem) => {
    const price = formatMoney(offer.priceMinor, offer.currency, language);
    return offer.type === "buy"
      ? t("offer.buyFor", { price })
      : t("offer.rentFor", { hours: offer.rentalHours ?? 48, price });
  };

  return (
    <article
      aria-label={movie.title}
      className="flex w-60 shrink-0 gap-3 rounded-md border bg-card p-2 text-card-foreground"
    >
      <Link to={`/movies/${movie.id}`} className="w-16 shrink-0">
        <Poster posterPath={movie.posterPath} title={movie.title} size="w185" />
      </Link>
      <div className="flex min-w-0 flex-1 flex-col gap-1 text-sm">
        <Link to={`/movies/${movie.id}`} className="line-clamp-2 font-medium hover:underline">
          {movie.title}
        </Link>
        <p className="flex items-center gap-2 text-xs text-muted-foreground">
          {movie.year !== null && <span>{movie.year}</span>}
          <span
            className="inline-flex items-center gap-0.5"
            aria-label={t("movie.ratingLabel", { rating: formatRating(movie.rating, language) })}
          >
            <Star className="size-3 fill-current" aria-hidden />
            {formatRating(movie.rating, language)}
          </span>
        </p>
        {movie.genres.length > 0 && (
          <p className="line-clamp-1 text-xs text-muted-foreground">{movie.genres.join(" · ")}</p>
        )}
        <div className="mt-auto flex flex-col gap-1 pt-1">
          {inCart ? (
            <p className="inline-flex items-center gap-1 text-xs font-medium">
              <Check className="size-3.5" aria-hidden />
              {t("chat.inCart")}
            </p>
          ) : (
            sortOffers(movie.offers).map((offer) => (
              <Button
                key={offer.id}
                size="sm"
                variant={offer.type === "buy" ? "default" : "outline"}
                className="h-7 justify-start px-2 text-xs"
                disabled={add.isPending}
                onClick={() => {
                  add.mutate({ body: { movieId: movie.id, offerType: offer.type } });
                }}
              >
                <ShoppingCart aria-hidden />
                {offerLabel(offer)}
              </Button>
            ))
          )}
          {add.isError && (
            <p role="alert" className="text-xs text-destructive">
              {commerceErrorMessage(t, add.error)}
            </p>
          )}
        </div>
      </div>
    </article>
  );
}
