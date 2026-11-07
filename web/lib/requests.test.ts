import { afterEach, describe, expect, it, vi } from "vitest";

import { canDestroy, followLogs, formatValue } from "@/lib/requests";
import { sseEvent, sseResponse } from "@/lib/test-sse";

describe("followLogs", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("reports statuses and lines, and resolves with the final status", async () => {
    const fetchMock = vi.fn(async () =>
      sseResponse([
        sseEvent("status", { status: "Applying" }),
        sseEvent("log", {
          id: 8,
          at: "t",
          stream: "Stdout",
          text: "Creating...",
        }),
        sseEvent("status", { status: "Succeeded" }),
        sseEvent("end", { status: "Succeeded" }),
      ]),
    );
    vi.stubGlobal("fetch", fetchMock);
    const onLine = vi.fn();
    const onStatus = vi.fn();

    const final = await followLogs("req-1", 0, { onLine, onStatus });

    expect(final).toBe("Succeeded");
    expect(onStatus.mock.calls).toEqual([["Applying"], ["Succeeded"]]);
    expect(onLine).toHaveBeenCalledWith(
      expect.objectContaining({ id: 8, text: "Creating..." }),
    );
    expect(fetchMock).toHaveBeenCalledWith(
      "/api/proxy/requests/req-1/logs",
      expect.objectContaining({
        headers: expect.not.objectContaining({ "Last-Event-ID": "0" }),
      }),
    );
  });

  it("resumes after the last line it has", async () => {
    const fetchMock = vi.fn(async () =>
      sseResponse([sseEvent("end", { status: "Destroyed" })]),
    );
    vi.stubGlobal("fetch", fetchMock);

    await followLogs("req-1", 42, { onLine: vi.fn(), onStatus: vi.fn() });

    expect(fetchMock).toHaveBeenCalledWith(
      "/api/proxy/requests/req-1/logs",
      expect.objectContaining({
        headers: expect.objectContaining({ "Last-Event-ID": "42" }),
      }),
    );
  });
});

describe("canDestroy", () => {
  it.each([
    ["Succeeded", true],
    ["Failed", true],
    ["AwaitingConfirmation", false],
    ["Queued", false],
    ["Applying", false],
    ["Destroying", false],
    ["Destroyed", false],
    ["Cancelled", false],
  ] as const)("is %s for %s", (status, expected) => {
    expect(canDestroy(status)).toBe(expected);
  });
});

describe("formatValue", () => {
  it("shows strings as they are and anything else as JSON", () => {
    expect(formatValue("lon1")).toBe("lon1");
    expect(formatValue(["a", 1])).toBe('["a",1]');
    expect(formatValue(null)).toBe("—");
  });
});
