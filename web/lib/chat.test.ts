import { afterEach, describe, expect, it, vi } from "vitest";

import { ApiError } from "@/lib/api";
import { type Proposal, sendMessage } from "@/lib/chat";
import { sseEvent, sseResponse } from "@/lib/test-sse";

const proposal: Proposal = {
  requestId: "req-1",
  type: "droplet",
  displayName: "Droplet",
  costHint: "About $4 a month.",
  items: [],
};

function handlers() {
  return { onText: vi.fn(), onProposal: vi.fn() };
}

describe("sendMessage", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("posts the text and calls the handlers as events arrive", async () => {
    const body =
      sseEvent("text", { text: "Hello " }) +
      sseEvent("proposal", proposal) +
      sseEvent("text", { text: "there" }) +
      sseEvent("done", {});
    const fetchMock = vi.fn(async () =>
      sseResponse([body.slice(0, 20), body.slice(20, 75), body.slice(75)]),
    );
    vi.stubGlobal("fetch", fetchMock);
    const h = handlers();

    await sendMessage("conv-1", "hi", h);

    expect(fetchMock).toHaveBeenCalledWith(
      "/api/proxy/conversations/conv-1/messages",
      expect.objectContaining({ method: "POST", body: '{"text":"hi"}' }),
    );
    expect(h.onText.mock.calls).toEqual([["Hello "], ["there"]]);
    expect(h.onProposal).toHaveBeenCalledWith(proposal);
  });

  it("throws the error event's message", async () => {
    vi.stubGlobal("fetch", async () =>
      sseResponse([
        sseEvent("text", { text: "Part" }),
        sseEvent("error", { error: "Claude is busy right now." }),
      ]),
    );

    const error = await sendMessage("conv-1", "hi", handlers()).catch(
      (e: unknown) => e,
    );

    expect(error).toBeInstanceOf(ApiError);
    expect(error).toMatchObject({
      message: "Claude is busy right now.",
      status: 0,
    });
  });

  it("explains the rate limit in plain words", async () => {
    vi.stubGlobal(
      "fetch",
      async () =>
        new Response(
          JSON.stringify({ title: "Too many messages", status: 429 }),
          { status: 429 },
        ),
    );

    const error = await sendMessage("conv-1", "hi", handlers()).catch(
      (e: unknown) => e,
    );

    expect(error).toMatchObject({ status: 429 });
    expect((error as Error).message).toMatch(/Wait a minute/);
  });

  it("throws when the stream ends without done", async () => {
    vi.stubGlobal("fetch", async () =>
      sseResponse([sseEvent("text", { text: "Part" })]),
    );

    await expect(sendMessage("conv-1", "hi", handlers())).rejects.toThrow(
      /ended unexpectedly/,
    );
  });
});
