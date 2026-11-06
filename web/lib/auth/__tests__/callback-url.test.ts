import { describe, expect, it } from "vitest";

import { DEFAULT_CALLBACK_PATH, toSafeCallbackPath } from "../callback-url";

const origin = "http://localhost:3000";

describe("toSafeCallbackPath", () => {
  it.each([
    ["/requests", "/requests"],
    ["/requests/42?tab=logs#top", "/requests/42?tab=logs#top"],
    ["http://localhost:3000/requests?x=1", "/requests?x=1"],
  ])("keeps the same-origin URL %s as %s", (callbackUrl, expected) => {
    expect(toSafeCallbackPath(callbackUrl, origin)).toBe(expected);
  });

  it.each([
    ["an absolute URL on another site", "https://evil.example/steal"],
    ["a protocol-relative URL", "//evil.example/steal"],
    ["the sign-in page", "/sign-in?callbackUrl=%2Fchat"],
    ["the root", "/"],
    ["an empty value", ""],
    ["a repeated parameter", ["/a", "/b"]],
    ["no value", undefined],
  ])("falls back to the default for %s", (_, callbackUrl) => {
    expect(toSafeCallbackPath(callbackUrl, origin)).toBe(DEFAULT_CALLBACK_PATH);
  });
});
