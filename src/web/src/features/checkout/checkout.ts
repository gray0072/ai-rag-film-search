import { apiClient } from "@/api/client";
import { ApiError } from "@/api/problem";
import type { OrderStatus } from "@/api/types";
import { newIdempotencyKey, redirectTo } from "@/lib/navigation";

/**
 * Creates an order from the cart and sends the browser to the payment page.
 * Each call is one checkout attempt with its own `Idempotency-Key`: a retried request of the same
 * attempt (network) gets the same order back instead of a second one. Throws an `ApiError`.
 */
export async function placeOrderAndPay(): Promise<void> {
  const created = await apiClient.POST("/api/orders", {
    params: { header: { "Idempotency-Key": newIdempotencyKey() } },
  });
  if (created.error) {
    throw new ApiError(created.error);
  }
  await payOrder(created.data.id);
}

/** Starts (or resumes) the payment session of an order and redirects to it. */
export async function payOrder(orderId: string): Promise<void> {
  const checkout = await apiClient.POST("/api/orders/{id}/checkout", {
    params: { path: { id: orderId } },
  });
  if (checkout.error) {
    throw new ApiError(checkout.error);
  }
  redirectTo(checkout.data.checkoutUrl);
}

/** The order is still waiting for the payment webhook (FR-COM-4: the redirect alone never pays it). */
export function isWaitingForPayment(status: OrderStatus | undefined): boolean {
  return status === undefined || status === "pendingPayment";
}
