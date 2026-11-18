import { useMutation } from "@tanstack/react-query";
import { useTranslation } from "react-i18next";
import { Link } from "react-router";
import type { OrderView } from "@/api/types";
import { Alert, AlertDescription } from "@/components/ui/alert";
import { Button } from "@/components/ui/button";
import { commerceErrorMessage } from "@/features/cart/commerceErrors";
import { currentLanguage } from "@/i18n";
import { formatMoney } from "@/lib/format";
import { payOrder } from "./checkout";

/** The lines and total of an order. */
export function OrderSummary({ order }: { order: OrderView }) {
  const { t } = useTranslation();
  const language = currentLanguage();

  return (
    <div className="rounded-md border">
      <ul aria-label={t("checkout.orderItems")} className="divide-y">
        {order.items.map((item) => (
          <li key={item.movieId} className="flex justify-between gap-4 p-3 text-sm">
            <span>
              {item.title}{" "}
              <span className="text-muted-foreground">
                {item.offerType === "buy"
                  ? t("offer.buy")
                  : t("offer.rentHours", { hours: item.rentalHours ?? 48 })}
              </span>
            </span>
            <span>{formatMoney(item.priceMinor, order.currency, language)}</span>
          </li>
        ))}
      </ul>
      <p className="flex justify-between border-t p-3 font-medium">
        <span>{t("cart.total")}</span>
        <span>{formatMoney(order.totalMinor, order.currency, language)}</span>
      </p>
    </div>
  );
}

/** "Pay again" for a failed or cancelled payment: a new attempt on the same order (spec 04, checkout). */
export function PayAgainButton({ orderId, label }: { orderId: string; label: string }) {
  const { t } = useTranslation();
  const pay = useMutation({ mutationFn: () => payOrder(orderId) });

  return (
    <div className="flex flex-col items-start gap-2">
      <Button
        disabled={pay.isPending || pay.isSuccess}
        onClick={() => {
          pay.mutate();
        }}
      >
        {pay.isPending || pay.isSuccess ? t("cart.redirecting") : label}
      </Button>
      {pay.isError && (
        <Alert variant="destructive">
          <AlertDescription>{commerceErrorMessage(t, pay.error)}</AlertDescription>
        </Alert>
      )}
    </div>
  );
}

export function BackToCartLink() {
  const { t } = useTranslation();
  return (
    <Button asChild variant="outline">
      <Link to="/cart">{t("checkout.backToCart")}</Link>
    </Button>
  );
}
