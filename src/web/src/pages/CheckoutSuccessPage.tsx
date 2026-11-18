import { useQueryClient } from "@tanstack/react-query";
import { CheckCircle2, Clock, XCircle } from "lucide-react";
import { useEffect, useState, type ReactNode } from "react";
import { useTranslation } from "react-i18next";
import { Link, useSearchParams } from "react-router";
import { api } from "@/api/client";
import { ErrorState, PageTitle } from "@/components/StatusMessages";
import { Button } from "@/components/ui/button";
import { cartQueryKey, libraryQueryKey } from "@/features/cart/cart";
import { isWaitingForPayment } from "@/features/checkout/checkout";
import { BackToCartLink, OrderSummary, PayAgainButton } from "@/features/checkout/OrderStatusView";

/** Polling interval while the order waits for the payment webhook. */
const pollMs = 1000;
/** After this time the page says that confirmation takes longer than usual and polls less often. */
const slowAfterMs = 20_000;

/**
 * Where the payment page sends the browser. The redirect proves nothing (FR-COM-4): the page polls
 * the order until the webhook has marked it paid or failed.
 */
export function CheckoutSuccessPage() {
  const { t } = useTranslation();
  const [searchParams] = useSearchParams();
  const orderId = searchParams.get("orderId") ?? "";
  const queryClient = useQueryClient();
  const [startedAt] = useState(() => Date.now());

  const order = api.useQuery(
    "get",
    "/api/orders/{id}",
    { params: { path: { id: orderId } } },
    {
      enabled: orderId !== "",
      refetchInterval: (query) => {
        if (!isWaitingForPayment(query.state.data?.status)) {
          return false;
        }
        return query.state.dataUpdatedAt - startedAt > slowAfterMs ? pollMs * 5 : pollMs;
      },
    },
  );
  const status = order.data?.status;
  const slow = isWaitingForPayment(status) && order.dataUpdatedAt - startedAt > slowAfterMs;

  // A paid order empties the cart and adds entitlements: refresh both.
  useEffect(() => {
    if (status === "paid") {
      void queryClient.invalidateQueries({ queryKey: cartQueryKey });
      void queryClient.invalidateQueries({ queryKey: libraryQueryKey });
    }
  }, [status, queryClient]);

  if (!orderId || (order.isError && !order.data)) {
    return (
      <section className="flex max-w-2xl flex-col gap-4">
        <PageTitle>{t("checkout.title")}</PageTitle>
        {orderId && order.isError ? (
          <ErrorState message={t("checkout.orderNotFound")} onRetry={() => void order.refetch()} />
        ) : (
          <ErrorState message={t("checkout.orderNotFound")} />
        )}
      </section>
    );
  }

  return (
    <section className="flex max-w-2xl flex-col gap-6">
      {status === "paid" ? (
        <>
          <StatusHeading icon={<CheckCircle2 className="size-7 text-green-600" aria-hidden />}>
            {t("checkout.paid.title")}
          </StatusHeading>
          <p>{t("checkout.paid.description")}</p>
          <div>
            <Button asChild>
              <Link to="/library">{t("checkout.paid.toLibrary")}</Link>
            </Button>
          </div>
        </>
      ) : status === "failed" ? (
        <>
          <StatusHeading icon={<XCircle className="size-7 text-destructive" aria-hidden />}>
            {t("checkout.failed.title")}
          </StatusHeading>
          <p>{t("checkout.failed.description")}</p>
          <div className="flex flex-wrap gap-2">
            <PayAgainButton orderId={orderId} label={t("checkout.failed.retry")} />
            <BackToCartLink />
          </div>
        </>
      ) : status === "expired" || status === "refunded" ? (
        <>
          <StatusHeading icon={<XCircle className="size-7 text-muted-foreground" aria-hidden />}>
            {status === "expired" ? t("checkout.expired.title") : t("checkout.refunded.title")}
          </StatusHeading>
          {status === "expired" && <p>{t("checkout.expired.description")}</p>}
          <div>
            <BackToCartLink />
          </div>
        </>
      ) : (
        <>
          <StatusHeading icon={<Clock className="size-7 animate-pulse" aria-hidden />}>
            {t("checkout.pending.title")}
          </StatusHeading>
          <p role="status">{t("checkout.pending.description")}</p>
          {slow && <p className="text-sm text-muted-foreground">{t("checkout.pending.slow")}</p>}
        </>
      )}

      {order.data && <OrderSummary order={order.data} />}
    </section>
  );
}

function StatusHeading({ icon, children }: { icon: ReactNode; children: ReactNode }) {
  return (
    <div className="flex items-center gap-3" aria-live="polite">
      {icon}
      <PageTitle>{children}</PageTitle>
    </div>
  );
}
