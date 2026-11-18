import { afterEach, describe, expect, it, vi } from "vitest";
import { apiClient } from "./client";

describe("apiClient", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("sends the first call alone, so parallel calls do not get different owner cookies", async () => {
    let releaseFirst: (response: Response) => void = () => undefined;
    const fetchMock = vi.fn((request: Request) =>
      new URL(request.url).pathname === "/api/info"
        ? new Promise<Response>((resolve) => {
            releaseFirst = resolve;
          })
        : Promise.resolve(Response.json([])),
    );
    vi.stubGlobal("fetch", fetchMock);

    const first = apiClient.GET("/api/info");
    const second = apiClient.GET("/api/chat/conversations");
    await new Promise((resolve) => setTimeout(resolve, 10));

    expect(fetchMock).toHaveBeenCalledTimes(1);

    releaseFirst(Response.json({ name: "FilmSearch", version: "1" }));
    await Promise.all([first, second]);

    expect(fetchMock).toHaveBeenCalledTimes(2);
    // Later calls are not serialized.
    await Promise.all([apiClient.GET("/api/cart"), apiClient.GET("/api/chat/conversations")]);
    expect(fetchMock).toHaveBeenCalledTimes(4);
  });
});
