import type { AssistantEvent, ConversationView, MessageView } from "@/api/types";
import { aCart, aMovie } from "./mockApi";

function encode(event: AssistantEvent): string {
  // The server sends the name both as the SSE event and as `type` in the data (spec 04).
  return `event: ${event.type}\ndata: ${JSON.stringify(event)}\n\n`;
}

/** A complete `text/event-stream` answer with the given events. */
export function sseResponse(events: AssistantEvent[]): Response {
  return new Response(events.map(encode).join(""), {
    headers: { "Content-Type": "text/event-stream" },
  });
}

/**
 * A stream the test writes to step by step (`send`, `close`): the mock stream to check the UI between events.
 * Aborting the request (the Stop button) ends the body with an AbortError, as a real fetch does.
 */
export function controlledSse() {
  const encoder = new TextEncoder();
  let controller: ReadableStreamDefaultController<Uint8Array> | undefined;
  let request: Request | undefined;
  const body = new ReadableStream<Uint8Array>({
    start(c) {
      controller = c;
    },
  });

  return {
    /** The route answer for `mockApi`. */
    answer: (r: Request) => {
      request = r;
      r.signal.addEventListener("abort", () => {
        controller?.error(new DOMException("The operation was aborted.", "AbortError"));
      });
      return new Response(body, { headers: { "Content-Type": "text/event-stream" } });
    },
    get request() {
      return request;
    },
    send(...events: AssistantEvent[]) {
      for (const event of events) {
        controller?.enqueue(encoder.encode(encode(event)));
      }
    },
    close() {
      controller?.close();
    },
  };
}

export const conversationId = "0190b000-0000-7000-8000-0000000000c1";
export const answerId = "0190b000-0000-7000-8000-0000000000a1";

/** A movie card of the assistant (spec 04 `MovieCard`) from the catalog test movie. */
export function aChatMovie(overrides: Partial<ReturnType<typeof aChatMovieBase>> = {}) {
  return { ...aChatMovieBase(), ...overrides };
}

function aChatMovieBase() {
  const movie = aMovie();
  return {
    id: movie.id,
    title: movie.title,
    year: movie.year,
    posterPath: movie.posterPath,
    genres: movie.genres.map((genre) => genre.name),
    rating: movie.voteAverage,
    offers: movie.offers,
    reason: null,
  };
}

export function aMessage(overrides: Partial<MessageView> = {}): MessageView {
  return {
    id: "0190b000-0000-7000-8000-0000000000m1",
    role: "user",
    text: "Hello",
    createdAt: "2026-11-18T10:00:00Z",
    movies: [],
    model: null,
    promptVersion: null,
    usage: null,
    latencyMs: null,
    feedback: null,
    confirmation: null,
    ...overrides,
  };
}

export function aConversation(messages: MessageView[] = []): ConversationView {
  return {
    id: conversationId,
    title: messages[0]?.text ?? null,
    locale: "en",
    createdAt: "2026-11-18T10:00:00Z",
    updatedAt: "2026-11-18T10:05:00Z",
    usage: { inputTokens: 0, outputTokens: 0, estimatedCostUsd: 0 },
    messages,
  };
}

/** The summary of a checkout confirmation: one line of the test movie, bought. */
export function aConfirmationSummary() {
  const cart = aCart([{ movieId: aMovie().id, title: "The Matrix", priceMinor: 999 }]);
  return { items: cart.items, totalMinor: cart.totalMinor, currency: cart.currency };
}
