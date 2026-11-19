import { screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import i18n from "@/i18n";
import { redirectTo } from "@/lib/navigation";
import { aCart, aMovie, anOrder, mockApi, problem, requestsTo } from "@/test/mockApi";
import { renderApp } from "@/test/renderApp";
import {
  aChatMovie,
  aConfirmationSummary,
  aConversation,
  aMessage,
  answerId,
  controlledSse,
  conversationId,
  sseResponse,
} from "@/test/sse";

vi.mock("@/lib/navigation", async (importOriginal) => ({
  ...(await importOriginal<typeof import("@/lib/navigation")>()),
  redirectTo: vi.fn(),
}));

const created = {
  id: conversationId,
  title: null,
  locale: "en",
  createdAt: "2026-11-18T10:00:00Z",
  updatedAt: "2026-11-18T10:00:00Z",
};
const confirmationId = "0190b000-0000-7000-8000-0000000000f9";
const inFuture = new Date(Date.now() + 15 * 60_000).toISOString();

const question = "Something for an evening, we liked Before Sunrise.";
const userMessage = aMessage({
  id: "0190b000-0000-7000-8000-0000000000u1",
  role: "user",
  text: question,
});
const answerMessage = aMessage({
  id: answerId,
  role: "assistant",
  text: "Try **The Matrix**.",
  movies: [aChatMovie()],
  model: "gemma",
});
const storedTurn = [userMessage, answerMessage];

/** The chat API of a new conversation whose first answer is the given stream. */
function chatRoutes(stream: unknown, history = storedTurn) {
  return {
    "GET /api/cart": aCart(),
    "GET /api/chat/conversations": [],
    "POST /api/chat/conversations": Response.json(created, { status: 201 }),
    "POST /api/chat/conversations/:id/messages": stream,
    "GET /api/chat/conversations/:id": aConversation(history),
  };
}

async function ask(text = question) {
  const user = userEvent.setup();
  await user.type(screen.getByRole("textbox", { name: "Message to the assistant" }), text);
  await user.click(screen.getByRole("button", { name: "Send" }));
  return user;
}

describe("ChatPage", () => {
  beforeEach(async () => {
    await i18n.changeLanguage("en");
    vi.mocked(redirectTo).mockClear();
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("streams an answer with tool progress and movie cards, then shows the stored history", async () => {
    const stream = controlledSse();
    const fetchMock = mockApi(chatRoutes(stream.answer));
    const { router } = renderApp("/chat");

    await ask();

    // A new conversation in the UI language, then the message; the URL gets the conversation id.
    await waitFor(() => {
      expect(router.state.location.pathname).toBe(`/chat/${conversationId}`);
    });
    const [create] = requestsTo(fetchMock, "/api/chat/conversations", "POST");
    expect(await create?.json()).toEqual({ locale: "en" });
    const messages = await screen.findByRole("list", { name: "Messages" });
    expect(within(messages).getByText(question)).toBeInTheDocument();
    expect(await screen.findByText("The assistant is thinking…")).toBeInTheDocument();

    stream.send(
      { type: "message.started", messageId: answerId },
      { type: "tool.started", callId: "c1", name: "search_movies", argsSummary: "romance" },
    );
    expect(await screen.findByText("Searching the catalog…")).toBeInTheDocument();
    expect(screen.queryByText("The assistant is thinking…")).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Stop the answer" })).toBeInTheDocument();

    stream.send(
      { type: "tool.completed", callId: "c1", name: "search_movies", ok: true },
      { type: "movies", items: [aChatMovie()] },
      { type: "message.delta", text: "Try **The " },
    );
    const card = await screen.findByRole("article", { name: "The Matrix" });
    expect(within(card).getByRole("button", { name: "Buy for $9.99" })).toBeInTheDocument();
    // Partial markdown is shown as it arrives.
    expect(await screen.findByText("Try **The")).toBeInTheDocument();

    stream.send(
      { type: "message.delta", text: "Matrix**." },
      {
        type: "message.completed",
        messageId: answerId,
        usage: { inputTokens: 10, outputTokens: 5 },
        model: "gemma",
        latencyMs: 900,
        fallback: false,
      },
    );
    stream.close();

    // Markdown is rendered (bold), the answer now comes from the reloaded history.
    const bold = await screen.findByText("The Matrix", { selector: "strong" });
    expect(bold).toBeInTheDocument();
    await waitFor(() => {
      expect(screen.queryByText("Searching the catalog…")).not.toBeInTheDocument();
    });
    expect(screen.getByRole("button", { name: "Send" })).toBeInTheDocument();
    expect(within(messages).getAllByRole("listitem", { name: /You|Assistant/ })).toHaveLength(2);
    expect(stream.request?.headers.get("Accept-Language")).toBe("en");
    expect(await stream.request?.json()).toEqual({ text: question });
  });

  it("stops the answer: the request is aborted and the partial text stays", async () => {
    const stream = controlledSse();
    mockApi(chatRoutes(stream.answer, [userMessage]));
    const user = userEvent.setup();
    renderApp("/chat");
    await ask();

    stream.send(
      { type: "message.started", messageId: answerId },
      { type: "message.delta", text: "Let me think about" },
    );
    expect(await screen.findByText("Let me think about")).toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "Stop the answer" }));

    expect(await screen.findByText("Stopped. This answer is not saved.")).toBeInTheDocument();
    expect(stream.request?.signal.aborted).toBe(true);
    expect(screen.getByText("Let me think about")).toBeInTheDocument();
    // The user message was stored before the answer started: it is not offered again.
    expect(screen.getByRole("textbox", { name: "Message to the assistant" })).toHaveValue("");
    expect(screen.getAllByText(question)).toHaveLength(1);
  });

  it("updates the cart badge on cart.updated and asks for a confirmation before payment", async () => {
    const cart = aCart([{ movieId: aMovie().id, title: "The Matrix", addedBy: "assistant" }]);
    const fetchMock = mockApi({
      ...chatRoutes(
        sseResponse([
          { type: "message.started", messageId: answerId },
          { type: "tool.started", callId: "c1", name: "add_to_cart", argsSummary: "" },
          { type: "tool.completed", callId: "c1", name: "add_to_cart", ok: true },
          { type: "cart.updated", cart },
          {
            type: "confirmation.required",
            confirmationId,
            kind: "checkout",
            summary: aConfirmationSummary(),
            expiresAt: inFuture,
          },
          { type: "message.delta", text: "Press **Go to payment**." },
          {
            type: "message.completed",
            messageId: answerId,
            usage: { inputTokens: 1, outputTokens: 1 },
            model: "gemma",
            latencyMs: 1,
            fallback: false,
          },
        ]),
        [
          aMessage({ role: "user", text: "Buy it" }),
          aMessage({
            id: answerId,
            role: "assistant",
            text: "Press **Go to payment**.",
            confirmation: {
              id: confirmationId,
              messageId: answerId,
              kind: "checkout",
              status: "pending",
              summary: aConfirmationSummary(),
              expiresAt: inFuture,
              orderId: null,
            },
          }),
        ],
      ),
      "POST /api/chat/conversations/:id/confirmations/:confirmationId": {
        status: "approved",
        orderId: "0190a000-0000-7000-8000-0000000000aa",
        checkoutUrl: "/fake-checkout/fake_cs_7",
        expiresAt: inFuture,
      },
    });
    renderApp("/chat");
    const user = await ask("Buy it");

    expect(await screen.findByRole("link", { name: "Cart, items: 1" })).toBeInTheDocument();
    const order = await screen.findByRole("region", { name: "Your order" });
    expect(within(order).getByText("The Matrix")).toBeInTheDocument();
    expect(within(order).getByTestId("confirmation-total")).toHaveTextContent("$9.99");
    // No order exists before the click (FR-AST-6).
    expect(requestsTo(fetchMock, "/api/orders", "POST")).toHaveLength(0);

    await user.click(within(order).getByRole("button", { name: "Go to payment" }));

    await waitFor(() => {
      expect(redirectTo).toHaveBeenCalledWith("/fake-checkout/fake_cs_7");
    });
    const [answer] = requestsTo(
      fetchMock,
      `/api/chat/conversations/${conversationId}/confirmations/${confirmationId}`,
      "POST",
    );
    expect(await answer?.json()).toEqual({ approved: true });
  });

  it("explains a changed cart instead of paying", async () => {
    const pending = aMessage({
      id: answerId,
      role: "assistant",
      text: "Ready.",
      confirmation: {
        id: confirmationId,
        messageId: answerId,
        kind: "checkout",
        status: "pending",
        summary: aConfirmationSummary(),
        expiresAt: inFuture,
        orderId: null,
      },
    });
    mockApi({
      "GET /api/cart": aCart(),
      "GET /api/chat/conversations": [created],
      "GET /api/chat/conversations/:id": aConversation([aMessage(), pending]),
      "POST /api/chat/conversations/:id/confirmations/:confirmationId": problem(409, "cartChanged"),
    });
    const user = userEvent.setup();
    renderApp(`/chat/${conversationId}`);

    await user.click(await screen.findByRole("button", { name: "Go to payment" }));

    expect(
      await screen.findByText(
        "Your cart has changed since the assistant prepared this order. Ask the assistant again.",
      ),
    ).toBeInTheDocument();
    expect(redirectTo).not.toHaveBeenCalled();
  });

  it("restores a conversation after a reload: history, cards, final confirmation state and rating", async () => {
    mockApi({
      "GET /api/cart": aCart(),
      "GET /api/chat/conversations": [{ ...created, title: question }],
      "GET /api/chat/conversations/:id": aConversation([
        userMessage,
        {
          ...answerMessage,
          feedback: { rating: 1, comment: null },
          confirmation: {
            id: confirmationId,
            messageId: answerId,
            kind: "checkout",
            status: "superseded",
            summary: aConfirmationSummary(),
            expiresAt: inFuture,
            orderId: null,
          },
        },
      ]),
    });
    renderApp(`/chat/${conversationId}`);

    expect(await screen.findByText("The Matrix", { selector: "strong" })).toBeInTheDocument();
    expect(screen.getByRole("article", { name: "The Matrix" })).toBeInTheDocument();
    expect(screen.getByText("A newer order summary replaced this one.")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Go to payment" })).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Good answer" })).toHaveAttribute(
      "aria-pressed",
      "true",
    );
    const list = screen.getByRole("navigation", { name: "Conversations" });
    expect(within(list).getByRole("link", { name: new RegExp(question) })).toHaveAttribute(
      "aria-current",
      "page",
    );
  });

  it("shows a paid order instead of the payment button", async () => {
    const order = anOrder({ status: "paid", source: "assistant" });
    mockApi({
      "GET /api/cart": aCart(),
      "GET /api/chat/conversations": [created],
      "GET /api/chat/conversations/:id": aConversation([
        userMessage,
        {
          ...answerMessage,
          confirmation: {
            id: confirmationId,
            messageId: answerId,
            kind: "checkout",
            status: "approved",
            summary: aConfirmationSummary(),
            expiresAt: inFuture,
            orderId: order.id,
          },
        },
      ]),
      "GET /api/orders/:id": order,
    });
    renderApp(`/chat/${conversationId}`);

    expect(await screen.findByText("This order is paid.")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Go to my library" })).toHaveAttribute(
      "href",
      "/library",
    );
    expect(screen.queryByRole("button", { name: "Open the payment page" })).not.toBeInTheDocument();
  });

  it("rates an answer and adds a card's movie to the cart", async () => {
    const fetchMock = mockApi({
      "GET /api/cart": aCart(),
      "GET /api/chat/conversations": [created],
      "GET /api/chat/conversations/:id": aConversation(storedTurn),
      "POST /api/chat/messages/:messageId/feedback": new Response(null, { status: 204 }),
      "POST /api/cart/items": aCart([{ movieId: aMovie().id, title: "The Matrix" }]),
    });
    const user = userEvent.setup();
    renderApp(`/chat/${conversationId}`);

    await user.click(await screen.findByRole("button", { name: "Bad answer" }));

    expect(await screen.findByText("Thank you for the feedback.")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Bad answer" })).toHaveAttribute(
      "aria-pressed",
      "true",
    );
    const [rating] = requestsTo(fetchMock, `/api/chat/messages/${answerId}/feedback`, "POST");
    expect(await rating?.json()).toEqual({ rating: -1 });

    await user.click(screen.getByRole("button", { name: "Rent for 48 h, $3.99" }));

    expect(await screen.findByText("In your cart")).toBeInTheDocument();
    const [added] = requestsTo(fetchMock, "/api/cart/items", "POST");
    expect(await added?.json()).toEqual({ movieId: aMovie().id, offerType: "rent" });
  });

  it("shows a stream error and gives the text back when the message was not stored", async () => {
    mockApi(
      chatRoutes(sseResponse([{ type: "error", code: "conversationBusy", message: "Busy" }]), []),
    );
    renderApp("/chat");
    await ask("Anything new?");

    expect(
      await screen.findByText(
        "The previous answer is still being written. Wait a moment and try again.",
      ),
    ).toBeInTheDocument();
    await waitFor(() => {
      expect(screen.getByRole("textbox", { name: "Message to the assistant" })).toHaveValue(
        "Anything new?",
      );
    });
  });

  it("shows the provider error of a started answer", async () => {
    mockApi(
      chatRoutes(
        sseResponse([
          { type: "message.started", messageId: answerId },
          { type: "error", code: "providerUnavailable", message: "Down" },
        ]),
        [userMessage],
      ),
    );
    renderApp("/chat");
    await ask();

    expect(
      await screen.findByText("The AI model is not available right now. Please try again later."),
    ).toBeInTheDocument();
    expect(screen.getByRole("textbox", { name: "Message to the assistant" })).toHaveValue("");
  });

  it("explains the rate limit and gives the text back", async () => {
    mockApi(
      chatRoutes(
        Response.json(
          { status: 429, title: "Too many requests.", code: "rateLimited", retryAfterSeconds: 3 },
          {
            status: 429,
            headers: { "Content-Type": "application/problem+json", "Retry-After": "3" },
          },
        ),
        [],
      ),
    );
    renderApp("/chat");
    await ask("One more?");

    expect(
      await screen.findByText("You are sending messages too fast. Wait a moment and try again."),
    ).toBeInTheDocument();
    await waitFor(() => {
      expect(screen.getByRole("textbox", { name: "Message to the assistant" })).toHaveValue(
        "One more?",
      );
    });
  });

  it("marks an answer of the backup model", async () => {
    mockApi({
      "GET /api/cart": aCart(),
      "GET /api/chat/conversations": [created],
      "GET /api/chat/conversations/:id": aConversation([
        userMessage,
        { ...answerMessage, fallback: true },
      ]),
    });
    renderApp(`/chat/${conversationId}`);

    expect(
      await screen.findByText("Answered by the backup AI model: the main one was not available."),
    ).toBeInTheDocument();
  });
});

describe("Chat drawer", () => {
  beforeEach(async () => {
    await i18n.changeLanguage("en");
  });

  afterEach(async () => {
    vi.unstubAllGlobals();
    await i18n.changeLanguage("en");
  });

  it("opens from any page and keeps the conversation", async () => {
    mockApi({
      ...chatRoutes(
        sseResponse([
          { type: "message.started", messageId: answerId },
          { type: "message.delta", text: "Try **The Matrix**." },
          {
            type: "message.completed",
            messageId: answerId,
            usage: { inputTokens: 1, outputTokens: 1 },
            model: "gemma",
            latencyMs: 1,
            fallback: false,
          },
        ]),
      ),
    });
    const user = userEvent.setup();
    renderApp("/");

    await user.click(screen.getByRole("button", { name: "Assistant" }));
    const drawer = await screen.findByRole("dialog", { name: "Assistant" });
    await user.type(within(drawer).getByRole("textbox"), question);
    await user.click(within(drawer).getByRole("button", { name: "Send" }));

    expect(
      await within(drawer).findByText("The Matrix", { selector: "strong" }),
    ).toBeInTheDocument();
    expect(within(drawer).getByRole("link", { name: "Open the chat page" })).toHaveAttribute(
      "href",
      `/chat/${conversationId}`,
    );

    await user.keyboard("{Escape}");
    await user.click(screen.getByRole("button", { name: "Assistant" }));
    expect(
      await within(await screen.findByRole("dialog")).findByText(question),
    ).toBeInTheDocument();
  });

  it("speaks the UI language", async () => {
    await i18n.changeLanguage("ru");
    mockApi({ "GET /api/cart": aCart(), "GET /api/chat/conversations": [] });
    renderApp("/chat");

    expect(screen.getByRole("heading", { level: 1, name: "Ассистент" })).toBeInTheDocument();
    expect(screen.getByRole("textbox", { name: "Сообщение ассистенту" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /«Перед рассветом»/ })).toBeInTheDocument();
  });
});
