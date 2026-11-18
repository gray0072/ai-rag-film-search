/** One server-sent event: the `event:` name (default `message`) and the joined `data:` lines. */
export interface ServerSentEvent {
  event: string;
  data: string;
}

/**
 * Reads a `text/event-stream` body (WHATWG HTML §9.2) and yields its events as they arrive.
 * `EventSource` cannot send a POST with a body, so the chat reads the `fetch` response stream itself.
 * Handles `\n`, `\r\n` and `\r` line ends, multi-line data, comments and chunks split anywhere.
 */
export async function* readServerSentEvents(
  body: ReadableStream<Uint8Array>,
): AsyncGenerator<ServerSentEvent> {
  const reader = body.getReader();
  // stream: true keeps a multi-byte character split between chunks for the next chunk.
  const decoder = new TextDecoder();
  let buffer = "";
  let event = "";
  let data: string[] = [];

  const dispatch = (): ServerSentEvent | undefined => {
    const result =
      data.length > 0 ? { event: event || "message", data: data.join("\n") } : undefined;
    event = "";
    data = [];
    return result;
  };

  try {
    for (;;) {
      const { done, value } = await reader.read();
      if (done) {
        break;
      }
      buffer += decoder.decode(value, { stream: true });

      for (;;) {
        const end = /\r\n|\r|\n/.exec(buffer);
        // A trailing "\r" may be the first half of "\r\n": wait for the next chunk.
        if (!end || (end[0] === "\r" && end.index === buffer.length - 1)) {
          break;
        }
        const line = buffer.slice(0, end.index);
        buffer = buffer.slice(end.index + end[0].length);

        if (line === "") {
          const item = dispatch();
          if (item) {
            yield item;
          }
          continue;
        }
        if (line.startsWith(":")) {
          continue;
        }
        const colon = line.indexOf(":");
        const field = colon === -1 ? line : line.slice(0, colon);
        let value = colon === -1 ? "" : line.slice(colon + 1);
        if (value.startsWith(" ")) {
          value = value.slice(1);
        }
        if (field === "event") {
          event = value;
        } else if (field === "data") {
          data.push(value);
        }
      }
    }
    // An event without the final blank line is incomplete and dropped (as browsers do).
  } finally {
    reader.releaseLock();
  }
}
