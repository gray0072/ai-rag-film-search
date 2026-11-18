import { useMutation } from "@tanstack/react-query";
import { CreditCard, Trash2 } from "lucide-react";
import { useTranslation } from "react-i18next";
import { Link } from "react-router";
import type { CartLine } from "@/api/types";
import { EmptyState, ErrorState, PageTitle } from "@/components/StatusMessages";
import { Alert, AlertDescription } from "@/components/ui/alert";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import { useCart, useRemoveFromCart } from "@/features/cart/cart";
import { commerceErrorMessage } from "@/features/cart/commerceErrors";
import { placeOrderAndPay } from "@/features/checkout/checkout";
import { Poster } from "@/features/movies/Poster";
import { currentLanguage } from "@/i18n";
import { formatMoney } from "@/lib/format";

/** FR-COM-1/2: the cart with DB prices and the checkout button. */
export function CartPage() {
  const { t } = useTranslation();
  const language = currentLanguage();
  const cart = useCart();
  const remove = useRemoveFromCart();
  // One click = one checkout attempt (its own Idempotency-Key); the button is locked while it runs.
  const checkout = useMutation({ mutationFn: placeOrderAndPay });

  if (cart.isPending) {
    return (
      <section className="flex flex-col gap-4" role="status" aria-label={t("common.loading")}>
        <PageTitle>{t("cart.title")}</PageTitle>
        <Skeleton className="h-24 w-full" />
        <Skeleton className="h-24 w-full" />
      </section>
    );
  }

  if (cart.isError) {
    return (
      <section className="flex flex-col gap-4">
        <PageTitle>{t("cart.title")}</PageTitle>
        <ErrorState onRetry={() => void cart.refetch()} />
      </section>
    );
  }

  const { items, totalMinor, currency } = cart.data;
  const hasUnavailable = items.some((line) => !line.available);

  return (
    <section className="flex max-w-3xl flex-col gap-6">
      <PageTitle>{t("cart.title")}</PageTitle>

      {items.length === 0 ? (
        <EmptyState>
          <p>{t("cart.empty")}</p>
          <Button asChild variant="link">
            <Link to="/">{t("cart.browse")}</Link>
          </Button>
        </EmptyState>
      ) : (
        <>
          <ul aria-label={t("cart.items")} className="flex flex-col divide-y rounded-md border">
            {items.map((line) => (
              <CartRow
                key={line.movieId}
                line={line}
                removing={remove.isPending && remove.variables.params.path.movieId === line.movieId}
                onRemove={() => {
                  remove.mutate({ params: { path: { movieId: line.movieId } } });
                }}
              />
            ))}
          </ul>

          {hasUnavailable && (
            <Alert variant="destructive">
              <AlertDescription>{t("cart.unavailableHint")}</AlertDescription>
            </Alert>
          )}

          <div className="flex flex-col items-end gap-3">
            <p className="text-lg">
              {t("cart.total")}{" "}
              <span className="font-semibold" data-testid="cart-total">
                {formatMoney(totalMinor, currency, language)}
              </span>
            </p>
            <Button
              size="lg"
              disabled={hasUnavailable || checkout.isPending || checkout.isSuccess}
              onClick={() => {
                checkout.mutate();
              }}
            >
              <CreditCard aria-hidden />
              {checkout.isPending || checkout.isSuccess
                ? t("cart.redirecting")
                : t("cart.checkout")}
            </Button>
            <p className="text-xs text-muted-foreground">{t("cart.fakePaymentNote")}</p>
            {checkout.isError && (
              <Alert variant="destructive">
                <AlertDescription>{commerceErrorMessage(t, checkout.error)}</AlertDescription>
              </Alert>
            )}
            {remove.isError && (
              <Alert variant="destructive">
                <AlertDescription>{commerceErrorMessage(t, remove.error)}</AlertDescription>
              </Alert>
            )}
          </div>
        </>
      )}
    </section>
  );
}

interface CartRowProps {
  line: CartLine;
  removing: boolean;
  onRemove: () => void;
}

function CartRow({ line, removing, onRemove }: CartRowProps) {
  const { t } = useTranslation();
  const language = currentLanguage();

  return (
    <li className="flex items-center gap-4 p-3">
      <Poster posterPath={line.posterPath} title={line.title} size="w185" className="w-14" />
      <div className="flex min-w-0 flex-1 flex-col gap-0.5">
        <Link to={`/movies/${line.movieId}`} className="truncate font-medium hover:underline">
          {line.title}
        </Link>
        <p className="text-sm text-muted-foreground">
          {line.offerType === "buy"
            ? t("offer.buy")
            : t("offer.rentHours", { hours: line.rentalHours ?? 48 })}
          {line.year !== null && ` · ${line.year}`}
          {line.addedBy === "assistant" && ` · ${t("cart.addedByAssistant")}`}
        </p>
        {!line.available && <p className="text-sm text-destructive">{t("cart.unavailable")}</p>}
      </div>
      <span className={line.available ? "font-medium" : "text-muted-foreground line-through"}>
        {formatMoney(line.priceMinor, line.currency, language)}
      </span>
      <Button
        variant="ghost"
        size="icon"
        disabled={removing}
        onClick={onRemove}
        aria-label={t("cart.remove", { title: line.title })}
      >
        <Trash2 aria-hidden />
      </Button>
    </li>
  );
}
