import { useMutation, useQueryClient } from "@tanstack/react-query";
import { CreditCard, ShieldCheck } from "lucide-react";
import type { TFunction } from "i18next";
import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { Link } from "react-router";
import { api, apiClient } from "@/api/client";
import { ApiError, problemCode } from "@/api/problem";
import type { ConfirmationView } from "@/api/types";
import { Button } from "@/components/ui/button";
import { commerceErrorMessage } from "@/features/cart/commerceErrors";
import { currentLanguage } from "@/i18n";
import { formatDateTime, formatMoney } from "@/lib/format";
import { redirectTo } from "@/lib/navigation";
import { conversationQueryKey } from "./chatApi";

/** Errors of the confirmations endpoint (spec 04) that need their own message. */
const confirmationErrorKeys = {
  cartChanged: "chat.confirmation.errors.cartChanged",
  confirmationExpired: "chat.confirmation.errors.expired",
  confirmationNotPending: "chat.confirmation.errors.notPending",
  confirmationNotFound: "chat.confirmation.errors.notFound",
} as const;

function confirmationErrorMessage(t: TFunction, error: unknown): string {
  const code = problemCode(error);
  return code !== undefined && Object.hasOwn(confirmationErrorKeys, code)
    ? t(confirmationErrorKeys[code as keyof typeof confirmationErrorKeys])
    : commerceErrorMessage(t, error);
}

async function answer(conversationId: string, confirmationId: string, approved: boolean) {
  const { data, error } = await apiClient.POST(
    "/api/chat/conversations/{id}/confirmations/{confirmationId}",
    { params: { path: { id: conversationId, confirmationId } }, body: { approved } },
  );
  if (error !== undefined) {
    throw new ApiError(error);
  }
  return data;
}

/** The current time, updated every `intervalMs`: a pending confirmation turns into "expired" on screen. */
function useNow(intervalMs: number): number {
  const [now, setNow] = useState(() => Date.now());
  useEffect(() => {
    const timer = setInterval(() => {
      setNow(Date.now());
    }, intervalMs);
    return () => {
      clearInterval(timer);
    };
  }, [intervalMs]);
  return now;
}

interface ConfirmationCardProps {
  conversationId: string;
  confirmation: ConfirmationView;
}

/**
 * FR-AST-6: the order the assistant prepared, with lines and total from the server. Only a click here creates
 * the order (the confirmations endpoint); then the browser goes to the payment page.
 */
export function ConfirmationCard({ conversationId, confirmation }: ConfirmationCardProps) {
  const { t } = useTranslation();
  const language = currentLanguage();
  const queryClient = useQueryClient();
  const { summary } = confirmation;
  const now = useNow(30_000);
  const expired =
    confirmation.status === "expired" ||
    (confirmation.status === "pending" && Date.parse(confirmation.expiresAt) <= now);
  const status = expired ? "expired" : confirmation.status;

  const refresh = () =>
    queryClient.invalidateQueries({ queryKey: conversationQueryKey(conversationId) });

  const approve = useMutation({
    mutationFn: () => answer(conversationId, confirmation.id, true),
    onSuccess: (result) => {
      if (result.checkoutUrl) {
        redirectTo(result.checkoutUrl);
      }
    },
    onError: () => void refresh(),
  });
  const decline = useMutation({
    mutationFn: () => answer(conversationId, confirmation.id, false),
    onSettled: () => void refresh(),
  });
  const busy = approve.isPending || decline.isPending || approve.isSuccess;
  const error = approve.error ?? decline.error;

  return (
    <section
      aria-label={t("chat.confirmation.title")}
      className="flex max-w-md flex-col gap-3 rounded-md border bg-card p-3 text-sm text-card-foreground"
    >
      <h3 className="flex items-center gap-2 font-medium">
        <ShieldCheck className="size-4" aria-hidden />
        {t("chat.confirmation.title")}
      </h3>
      <ul className="flex flex-col gap-1">
        {summary.items.map((line) => (
          <li key={line.movieId} className="flex justify-between gap-3">
            <span className="min-w-0 truncate">
              {line.title}
              <span className="text-muted-foreground">
                {" · "}
                {line.offerType === "buy"
                  ? t("offer.buy")
                  : t("offer.rentHours", { hours: line.rentalHours ?? 48 })}
              </span>
            </span>
            <span>{formatMoney(line.priceMinor, line.currency, language)}</span>
          </li>
        ))}
      </ul>
      <p className="flex justify-between border-t pt-2 font-medium">
        <span>{t("cart.total")}</span>
        <span data-testid="confirmation-total">
          {formatMoney(summary.totalMinor, summary.currency, language)}
        </span>
      </p>

      {status === "pending" && (
        <>
          <div className="flex flex-wrap items-center gap-2">
            <Button
              disabled={busy}
              onClick={() => {
                approve.mutate();
              }}
            >
              <CreditCard aria-hidden />
              {approve.isPending || approve.isSuccess ? t("cart.redirecting") : t("cart.checkout")}
            </Button>
            <Button
              variant="ghost"
              disabled={busy}
              onClick={() => {
                decline.mutate();
              }}
            >
              {t("chat.confirmation.decline")}
            </Button>
          </div>
          <p className="text-xs text-muted-foreground">
            {t("chat.confirmation.validUntil", {
              time: formatDateTime(confirmation.expiresAt, language),
            })}{" "}
            {t("cart.fakePaymentNote")}
          </p>
        </>
      )}
      {status === "approved" && confirmation.orderId !== null && (
        <ApprovedOrder
          orderId={confirmation.orderId}
          busy={busy}
          onPay={() => {
            approve.mutate();
          }}
        />
      )}
      {status === "declined" && (
        <p className="text-muted-foreground">{t("chat.confirmation.declined")}</p>
      )}
      {status === "superseded" && (
        <p className="text-muted-foreground">{t("chat.confirmation.superseded")}</p>
      )}
      {status === "expired" && (
        <p className="text-muted-foreground">{t("chat.confirmation.expired")}</p>
      )}
      {error && (
        <p role="alert" className="text-destructive">
          {confirmationErrorMessage(t, error)}
        </p>
      )}
    </section>
  );
}

interface ApprovedOrderProps {
  orderId: string;
  busy: boolean;
  onPay: () => void;
}

/** After the click: the order's own status, so a paid order no longer offers the payment page. */
function ApprovedOrder({ orderId, busy, onPay }: ApprovedOrderProps) {
  const { t } = useTranslation();
  const order = api.useQuery("get", "/api/orders/{id}", { params: { path: { id: orderId } } });
  const status = order.data?.status;

  if (status === "paid") {
    return (
      <p className="text-muted-foreground">
        {t("chat.confirmation.paid")}{" "}
        <Link to="/library" className="underline">
          {t("checkout.paid.toLibrary")}
        </Link>
      </p>
    );
  }
  if (status !== undefined && status !== "pendingPayment") {
    return <p className="text-muted-foreground">{t("chat.confirmation.notPaid")}</p>;
  }
  return (
    <div className="flex flex-wrap items-center gap-2">
      <p className="text-muted-foreground">{t("chat.confirmation.approved")}</p>
      <Button variant="outline" size="sm" disabled={busy} onClick={onPay}>
        {t("chat.confirmation.openPayment")}
      </Button>
    </div>
  );
}
