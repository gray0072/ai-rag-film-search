import { screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { OrderView } from "@/api/types";
import i18n from "@/i18n";
import { redirectTo } from "@/lib/navigation";
import { aCart, anOrder, mockApi, requestsTo } from "@/test/mockApi";
import { renderApp } from "@/test/renderApp";

vi.mock("@/lib/navigation", async (importOriginal) => ({
  ...(await importOriginal<typeof import("@/lib/navigation")>()),
  redirectTo: vi.fn(),
}));

const order = anOrder();

/** The order answers `pendingPayment` until the given number of polls, then the final status. */
function stubOrder(final: Partial<OrderView>, pendingPolls = 1) {
  let polls = 0;
  return mockApi({
    "GET /api/cart": aCart(),
    "GET /api/library": [],
    "GET /api/orders/:id": () => {
      polls += 1;
      return polls <= pendingPolls ? order : { ...order, ...final };
    },
    "POST /api/orders/:id/checkout": {
      orderId: order.id,
      checkoutUrl: "/fake-checkout/fake_cs_2",
      expiresAt: order.expiresAt,
    },
  });
}

describe("CheckoutSuccessPage", () => {
  beforeEach(async () => {
    await i18n.changeLanguage("en");
    vi.mocked(redirectTo).mockClear();
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("polls the order until the webhook has marked it paid", async () => {
    const fetchMock = stubOrder({ status: "paid", paidAt: "2026-11-18T10:01:00Z" });
    renderApp(`/checkout/success?orderId=${order.id}`);

    expect(await screen.findByText("Confirming the payment…")).toBeInTheDocument();
    expect(await screen.findByText("Thank you!", {}, { timeout: 3000 })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Go to my library" })).toHaveAttribute(
      "href",
      "/library",
    );
    expect(screen.getByText("The Matrix")).toBeInTheDocument();
    // Paid: the cart and the library are fetched again.
    await waitFor(() => {
      expect(requestsTo(fetchMock, "/api/cart").length).toBeGreaterThanOrEqual(2);
    });
  });

  it("offers to pay again after a failed payment", async () => {
    stubOrder({ status: "failed" }, 0);
    const user = userEvent.setup();
    renderApp(`/checkout/success?orderId=${order.id}`);

    await user.click(await screen.findByRole("button", { name: "Try to pay again" }));

    await waitFor(() => {
      expect(redirectTo).toHaveBeenCalledWith("/fake-checkout/fake_cs_2");
    });
  });

  it("says so when the order id is missing", () => {
    mockApi({ "GET /api/cart": aCart() });
    renderApp("/checkout/success");

    expect(screen.getByText("This order was not found.")).toBeInTheDocument();
  });
});

describe("CheckoutCancelPage", () => {
  beforeEach(async () => {
    await i18n.changeLanguage("en");
    vi.mocked(redirectTo).mockClear();
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("keeps the order payable and returns to the payment page", async () => {
    stubOrder({}, 100);
    const user = userEvent.setup();
    renderApp(`/checkout/cancel?orderId=${order.id}`);

    expect(await screen.findByText(/Your order is kept until/)).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Back to cart" })).toHaveAttribute("href", "/cart");

    await user.click(screen.getByRole("button", { name: "Return to payment" }));

    await waitFor(() => {
      expect(redirectTo).toHaveBeenCalledWith("/fake-checkout/fake_cs_2");
    });
  });
});
