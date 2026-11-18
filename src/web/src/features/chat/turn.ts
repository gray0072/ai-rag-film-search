import type { AssistantEvent, AssistantMovie, ConfirmationView } from "@/api/types";

export interface ToolCall {
  callId: string;
  name: string;
  status: "running" | "ok" | "failed";
}

/**
 * The answer being streamed (or the last one that did not complete). After a completed answer the
 * conversation is reloaded from the server and the turn is dropped: history is the source of truth.
 */
export interface Turn {
  /** The conversation the turn is shown in; "" for a new one until the page has switched to `createdId`. */
  conversationId: string;
  /** The conversation this turn created (its first message). */
  createdId?: string;
  userText: string;
  /** Visible history messages when the turn started: newer ones are hidden until the turn settles. */
  historyLength: number;
  /** `message.started` arrived: the server has stored the user message. */
  userStored: boolean;
  messageId?: string;
  text: string;
  tools: ToolCall[];
  movies: AssistantMovie[];
  confirmation?: ConfirmationView;
  status: "streaming" | "completed" | "stopped" | "failed";
  /** An error code of the stream (`providerUnavailable`, …) or of the request. */
  error?: string;
  /** The history was reloaded after the end of the turn. */
  settled: boolean;
}

export function startTurn(conversationId: string, userText: string, historyLength: number): Turn {
  return {
    conversationId,
    userText,
    historyLength,
    userStored: false,
    text: "",
    tools: [],
    movies: [],
    status: "streaming",
    settled: false,
  };
}

/** Applies one SSE event to the turn (pure, so the order of events is easy to test). */
export function applyEvent(turn: Turn, event: AssistantEvent): Turn {
  switch (event.type) {
    case "message.started":
      return { ...turn, userStored: true, messageId: event.messageId };
    case "message.delta":
      return { ...turn, text: turn.text + event.text };
    case "tool.started":
      return {
        ...turn,
        tools: [...turn.tools, { callId: event.callId, name: event.name, status: "running" }],
      };
    case "tool.completed":
      return {
        ...turn,
        tools: turn.tools.map((tool) =>
          tool.callId === event.callId ? { ...tool, status: event.ok ? "ok" : "failed" } : tool,
        ),
      };
    case "movies": {
      const known = new Set(turn.movies.map((movie) => movie.id));
      return { ...turn, movies: [...turn.movies, ...event.items.filter((m) => !known.has(m.id))] };
    }
    case "confirmation.required":
      return {
        ...turn,
        confirmation: {
          id: event.confirmationId,
          messageId: turn.messageId ?? "",
          kind: event.kind,
          status: "pending",
          summary: event.summary,
          expiresAt: event.expiresAt,
          orderId: null,
        },
      };
    case "message.completed":
      return { ...turn, status: "completed", messageId: event.messageId };
    case "error":
      return { ...turn, status: "failed", error: event.code };
    case "cart.updated":
      // The cart lives in the query cache, not in the turn.
      return turn;
  }
}
