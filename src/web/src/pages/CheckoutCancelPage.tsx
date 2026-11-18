import { useTranslation } from "react-i18next";
import { useSearchParams } from "react-router";
import { api } from "@/api/client";
import { PageTitle } from "@/components/StatusMessages";
import { Skeleton } from "@/components/ui/skeleton";
import { BackToCartLink, OrderSummary, PayAgainButton } from "@/features/checkout/OrderStatusView";
import { currentLanguage } from "@/i18n";
import { formatDateTime } from "@/lib/format";

/** Where the payment page sends the browser on "Cancel": the order stays payable until it expires. */
export function CheckoutCancelPage() {
  const { t } = useTranslation();
  const language = currentLanguage();
  const [searchParams] = useSearchParams();
  const orderId = searchParams.get("orderId") ?? "";
  const order = api.useQuery(
    "get",
    "/api/orders/{id}",
    { params: { path: { id: orderId } } },
    { enabled: orderId !== "" },
  );
  const payable = order.data?.status === "pendingPayment" || order.data?.status === "failed";

  return (
    <section className="flex max-w-2xl flex-col gap-6">
      <PageTitle>{t("checkout.cancelled.title")}</PageTitle>
      {orderId && order.isPending ? (
        <Skeleton className="h-24 w-full" />
      ) : (
        <>
          <p>
            {payable && order.data
              ? t("checkout.cancelled.description", {
                  date: formatDateTime(order.data.expiresAt, language),
                })
              : t("checkout.cancelled.notPayable")}
          </p>
          <div className="flex flex-wrap gap-2">
            {payable && <PayAgainButton orderId={orderId} label={t("checkout.cancelled.resume")} />}
            <BackToCartLink />
          </div>
          {order.data && <OrderSummary order={order.data} />}
        </>
      )}
    </section>
  );
}
