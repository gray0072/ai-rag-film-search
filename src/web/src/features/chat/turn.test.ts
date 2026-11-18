import { describe, expect, it } from "vitest";
import { aChatMovie } from "@/test/sse";
import { toAssistantEvent } from "./chatApi";
import { applyEvent, startTurn } from "./turn";

describe("applyEvent", () => {
  it("tracks tools by call id and keeps movie cards unique", () => {
    let turn = startTurn("c", "hi", 0);
    turn = applyEvent(turn, { type: "message.started", messageId: "m" });
    turn = applyEvent(turn, {
      type: "tool.started",
      callId: "1",
      name: "search_movies",
      argsSummary: "",
    });
    turn = applyEvent(turn, {
      type: "tool.started",
      callId: "2",
      name: "add_to_cart",
      argsSummary: "",
    });
    turn = applyEvent(turn, {
      type: "tool.completed",
      callId: "2",
      name: "add_to_cart",
      ok: false,
    });
    turn = applyEvent(turn, { type: "movies", items: [aChatMovie()] });
    turn = applyEvent(turn, { type: "movies", items: [aChatMovie(), aChatMovie({ id: "other" })] });

    expect(turn.userStored).toBe(true);
    expect(turn.tools.map((tool) => tool.status)).toEqual(["running", "failed"]);
    expect(turn.movies.map((movie) => movie.id)).toEqual([aChatMovie().id, "other"]);
  });

  it("binds a confirmation to the answer and ends on an error", () => {
    let turn = applyEvent(startTurn("c", "buy it", 2), { type: "message.started", messageId: "m" });
    turn = applyEvent(turn, {
      type: "confirmation.required",
      confirmationId: "k",
      kind: "checkout",
      summary: { items: [], totalMinor: 999, currency: "USD" },
      expiresAt: "2026-11-18T10:15:00Z",
    });
    turn = applyEvent(turn, { type: "error", code: "providerUnavailable", message: "Down" });

    expect(turn.confirmation).toMatchObject({ id: "k", messageId: "m", status: "pending" });
    expect(turn).toMatchObject({ status: "failed", error: "providerUnavailable" });
  });
});

describe("toAssistantEvent", () => {
  it("takes the type from the SSE event name and skips unknown or broken events", () => {
    expect(toAssistantEvent({ event: "message.delta", data: '{"text":"a"}' })).toEqual({
      type: "message.delta",
      text: "a",
    });
    expect(toAssistantEvent({ event: "usage.updated", data: "{}" })).toBeUndefined();
    expect(toAssistantEvent({ event: "message.delta", data: "not json" })).toBeUndefined();
  });
});
