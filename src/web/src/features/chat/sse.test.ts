import { describe, expect, it } from "vitest";
import { readServerSentEvents, type ServerSentEvent } from "./sse";

function streamOf(...chunks: string[]): ReadableStream<Uint8Array> {
  const encoder = new TextEncoder();
  return new ReadableStream({
    start(controller) {
      for (const chunk of chunks) {
        controller.enqueue(encoder.encode(chunk));
      }
      controller.close();
    },
  });
}

async function readAll(stream: ReadableStream<Uint8Array>): Promise<ServerSentEvent[]> {
  const events: ServerSentEvent[] = [];
  for await (const event of readServerSentEvents(stream)) {
    events.push(event);
  }
  return events;
}

describe("readServerSentEvents", () => {
  it("reads named events with JSON data", async () => {
    const events = await readAll(
      streamOf(
        'event: message.started\ndata: {"messageId":"1"}\n\n',
        'event: message.delta\ndata: {"text":"Hi"}\n\n',
      ),
    );

    expect(events).toEqual([
      { event: "message.started", data: '{"messageId":"1"}' },
      { event: "message.delta", data: '{"text":"Hi"}' },
    ]);
  });

  it("joins chunks split inside lines and across CRLF", async () => {
    const events = await readAll(
      streamOf("event: message.de", "lta\r", '\ndata: {"te', 'xt":"a"}\r\n\r', "\n"),
    );

    expect(events).toEqual([{ event: "message.delta", data: '{"text":"a"}' }]);
  });

  it("joins multi-line data, skips comments and uses the default event name", async () => {
    const events = await readAll(streamOf(": keep-alive\n\ndata: one\ndata:two\n\n"));

    expect(events).toEqual([{ event: "message", data: "one\ntwo" }]);
  });

  it("drops an event without the closing blank line", async () => {
    const events = await readAll(streamOf("event: a\ndata: 1\n\nevent: b\ndata: 2\n"));

    expect(events).toEqual([{ event: "a", data: "1" }]);
  });

  it("decodes multi-byte characters split between chunks", async () => {
    const bytes = new TextEncoder().encode('data: {"text":"Привет"}\n\n');
    const stream = new ReadableStream<Uint8Array>({
      start(controller) {
        controller.enqueue(bytes.slice(0, 15));
        controller.enqueue(bytes.slice(15));
        controller.close();
      },
    });

    const events = await readAll(stream);

    expect(events).toEqual([{ event: "message", data: '{"text":"Привет"}' }]);
  });
});
