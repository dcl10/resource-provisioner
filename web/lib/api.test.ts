import { describe, expect, it } from "vitest";

import { ApiError, retryUnlessClientError } from "@/lib/api";

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
