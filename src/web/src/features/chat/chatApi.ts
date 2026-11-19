import { api, apiClient } from "@/api/client";
import { ApiError } from "@/api/problem";
import type { AssistantEvent } from "@/api/types";
import { readServerSentEvents, type ServerSentEvent } from "./sse";

/** Query key of the conversation list. */
export const conversationsQueryKey = api.queryOptions("get", "/api/chat/conversations").queryKey;

/** Query key of one conversation with its messages. */
export function conversationQueryKey(id: string) {
  return api.queryOptions("get", "/api/chat/conversations/{id}", { params: { path: { id } } })
    .queryKey;
}

const eventTypes = new Set<string>([
  "message.started",
  "message.delta",
  "tool.started",
  "tool.completed",
  "movies",
  "cart.updated",
  "confirmation.required",
  "message.completed",
  "error",
] satisfies AssistantEvent["type"][]);

/** The typed event of an SSE item; unknown event names (a newer server) and broken JSON are skipped. */
export function toAssistantEvent(item: ServerSentEvent): AssistantEvent | undefined {
  if (!eventTypes.has(item.event)) {
    return undefined;
  }
  try {
    const data: unknown = JSON.parse(item.data);
    if (typeof data !== "object" || data === null) {
      return undefined;
    }
    return { ...data, type: item.event } as AssistantEvent;
  } catch {
    return undefined;
  }
}

/**
 * Sends a user message and calls `onEvent` for every event of the streamed answer (FR-AST-1).
 * Aborting `signal` closes the connection, which cancels the model call on the server (spec 06 Â§7).
 * Throws an `ApiError` when the request is refused before the stream starts (400, 404, 429),
 * and the `AbortError` of the signal when it is aborted.
 */
export async function streamAnswer(
  conversationId: string,
  text: string,
  signal: AbortSignal,
  onEvent: (event: AssistantEvent) => void,
): Promise<void> {
  const { response, error } = await apiClient.POST("/api/chat/conversations/{id}/messages", {
    params: { path: { id: conversationId } },
    body: { text },
    headers: { Accept: "text/event-stream" },
    parseAs: "stream",
    signal,
  });
  if (error !== undefined || !response.body) {
    throw new ApiError(error ?? { status: response.status, title: response.statusText });
  }

  for await (const item of readServerSentEvents(response.body)) {
    signal.throwIfAborted();
    const event = toAssistantEvent(item);
    if (event) {
      onEvent(event);
    }
  }
  signal.throwIfAborted();
}

/** Starts a conversation in the UI language. */
export async function createConversation(locale: string): Promise<string> {
  const { data, error } = await apiClient.POST("/api/chat/conversations", {
    body: { locale },
  });
  if (data) {
    return data.id;
  }
  // A Problem Details body (the client middleware makes one for empty errors); 429 has `code: rateLimited`.
  throw new ApiError(error);
}
