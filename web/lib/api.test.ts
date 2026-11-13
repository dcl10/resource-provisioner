import { afterEach, describe, expect, it, vi } from "vitest";

import {
  ApiError,
  apiFetch,
  isNotFound,
  retryUnlessClientError,
} from "@/lib/api";

afterEach(() => {
  vi.unstubAllGlobals();
});

describe("apiFetch", () => {
  it("resolves with nothing when the API answers 204", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn(async () => new Response(null, { status: 204 })),
    );

    await expect(
      apiFetch("/requests/r/cancel", { method: "POST" }),
    ).resolves.toBeUndefined();
  });
});

describe("isNotFound", () => {
  it("is true only for the API's 404", () => {
    expect(isNotFound(new ApiError("Not found", 404))).toBe(true);
    expect(isNotFound(new ApiError("Forbidden", 403))).toBe(false);
    expect(isNotFound(new TypeError("fetch failed"))).toBe(false);
    expect(isNotFound(null)).toBe(false);
  });
});

describe("retryUnlessClientError", () => {
  it("never retries a 4xx", () => {
    expect(retryUnlessClientError(0, new ApiError("Not found", 404))).toBe(
      false,
    );
    expect(retryUnlessClientError(0, new ApiError("Forbidden", 403))).toBe(
      false,
    );
  });

  it("retries server and network errors up to three times", () => {
    expect(retryUnlessClientError(0, new ApiError("Down", 502))).toBe(true);
    expect(retryUnlessClientError(2, new TypeError("fetch failed"))).toBe(true);
    expect(retryUnlessClientError(3, new ApiError("Down", 502))).toBe(false);
  });
});
