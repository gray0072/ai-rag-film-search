import { screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import i18n from "@/i18n";
import { redirectTo } from "@/lib/navigation";
import { aCart, anOrder, mockApi, problem, requestsTo } from "@/test/mockApi";
import { renderApp } from "@/test/renderApp";

vi.mock("@/lib/navigation", async (importOriginal) => ({
  ...(await importOriginal<typeof import("@/lib/navigation")>()),
  redirectTo: vi.fn(),
}));

const twoLines = aCart([
  { title: "The Matrix", offerType: "buy", priceMinor: 999 },
  { title: "Heat", offerType: "rent", priceMinor: 399, rentalHours: 48, addedBy: "assistant" },
]);

describe("CartPage", () => {
  beforeEach(async () => {
    await i18n.changeLanguage("en");
    vi.mocked(redirectTo).mockClear();
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("lists the lines with DB prices and removes one", async () => {
    const fetchMock = mockApi({
      "GET /api/cart": twoLines,
      "DELETE /api/cart/items/:movieId": aCart([{ title: "Heat", priceMinor: 399 }]),
    });
    const user = userEvent.setup();
    renderApp("/cart");

    const list = await screen.findByRole("list", { name: "Cart items" });
    expect(within(list).getAllByRole("listitem")).toHaveLength(2);
    expect(within(list).getByText(/Rent for 48 h/)).toBeInTheDocument();
    expect(within(list).getByText(/added by the assistant/)).toBeInTheDocument();
    expect(screen.getByTestId("cart-total")).toHaveTextContent("$13.98");

    await user.click(screen.getByRole("button", { name: "Remove The Matrix from the cart" }));

    expect(
      requestsTo(fetchMock, `/api/cart/items/${twoLines.items[0]?.movieId ?? ""}`, "DELETE"),
    ).toHaveLength(1);
    await waitFor(() => {
      expect(screen.getByTestId("cart-total")).toHaveTextContent("$3.99");
    });
  });

  it("creates the order with an idempotency key and redirects to the payment page", async () => {
    const order = anOrder();
    const fetchMock = mockApi({
      "GET /api/cart": twoLines,
      "POST /api/orders": Response.json(order, { status: 201 }),
      "POST /api/orders/:id/checkout": {
        orderId: order.id,
        checkoutUrl: "/fake-checkout/fake_cs_1",
        expiresAt: order.expiresAt,
      },
    });
    const user = userEvent.setup();
    renderApp("/cart");

    await user.click(await screen.findByRole("button", { name: "Go to payment" }));

    await waitFor(() => {
      expect(redirectTo).toHaveBeenCalledWith("/fake-checkout/fake_cs_1");
    });
    const [created] = requestsTo(fetchMock, "/api/orders", "POST");
    expect(created?.headers.get("Idempotency-Key")).toMatch(/^[0-9a-f-]{36}$/);
    expect(requestsTo(fetchMock, `/api/orders/${order.id}/checkout`, "POST")).toHaveLength(1);
    expect(screen.getByRole("button", { name: "Opening the payment page…" })).toBeDisabled();
  });

  it("uses a new idempotency key for each checkout attempt", async () => {
    const fetchMock = mockApi({
      "GET /api/cart": twoLines,
      "POST /api/orders": problem(409, "alreadyOwned"),
    });
    const user = userEvent.setup();
    renderApp("/cart");
    const button = await screen.findByRole("button", { name: "Go to payment" });

    await user.click(button);
    expect(await screen.findByText("You already own this movie.")).toBeInTheDocument();
    await user.click(button);

    await waitFor(() => {
      expect(requestsTo(fetchMock, "/api/orders", "POST")).toHaveLength(2);
    });
    const keys = requestsTo(fetchMock, "/api/orders", "POST").map((request) =>
      request.headers.get("Idempotency-Key"),
    );
    expect(new Set(keys).size).toBe(2);
    expect(redirectTo).not.toHaveBeenCalled();
  });

  it("blocks checkout while a line is no longer sold", async () => {
    mockApi({
      "GET /api/cart": aCart([{ title: "Gone", available: false }, { title: "Heat" }]),
    });
    renderApp("/cart");

    expect(await screen.findByText("No longer sold")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Go to payment" })).toBeDisabled();
  });

  it("shows the empty cart", async () => {
    mockApi({ "GET /api/cart": aCart() });
    renderApp("/cart");

    expect(await screen.findByText("Your cart is empty.")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Browse the catalog" })).toHaveAttribute("href", "/");
  });
});
