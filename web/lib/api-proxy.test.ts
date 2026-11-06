import { describe, expect, it } from "vitest";

import {
  buildApiUrl,
  buildDownstreamHeaders,
  buildUpstreamHeaders,
} from "@/lib/api-proxy";

const api = "http://localhost:5000";

describe("buildApiUrl", () => {
  it("maps the proxy path and query onto the API", () => {
    expect(buildApiUrl(api, "/api/proxy/me", "")?.toString()).toBe(
      "http://localhost:5000/api/me",
    );
    expect(
      buildApiUrl(api, "/api/proxy/requests/42/logs", "?after=10")?.toString(),
    ).toBe("http://localhost:5000/api/requests/42/logs?after=10");
  });

  it.each([
    ["the bare proxy prefix", "/api/proxy"],
    ["a path outside the proxy", "/chat"],
    ["a path climbing out of /api", "/api/proxy/../../health"],
    ["an encoded climb out of /api", "/api/proxy/%2e%2e/%2e%2e/health"],
  ])("rejects %s", (_, pathname) => {
    expect(buildApiUrl(api, pathname, "")).toBeNull();
  });

  it("keeps a doubled slash on the API's origin", () => {
    expect(buildApiUrl(api, "/api/proxy//evil.example/x", "")?.origin).toBe(
      api,
    );
  });
});

describe("buildUpstreamHeaders", () => {
  it("adds the bearer token and passes only the allowed headers", () => {
    const headers = buildUpstreamHeaders(
      new Headers({
        accept: "text/event-stream",
        "content-type": "application/json",
        cookie: "authjs.session-token=secret",
        authorization: "Bearer from-the-browser",
        "x-forwarded-for": "1.2.3.4",
      }),
      "access-token",
    );

    expect(Object.fromEntries(headers)).toEqual({
      accept: "text/event-stream",
      "content-type": "application/json",
      authorization: "Bearer access-token",
    });
  });
});

describe("buildDownstreamHeaders", () => {
  it("drops hop-by-hop, encoding, length and cookie headers", () => {
    const headers = buildDownstreamHeaders(
      new Headers({
        "content-type": "application/json",
        "content-encoding": "gzip",
        "content-length": "42",
        connection: "keep-alive",
        "set-cookie": "api=1",
        location: "/api/requests/42",
      }),
    );

    expect(Object.fromEntries(headers)).toEqual({
      "content-type": "application/json",
      location: "/api/requests/42",
    });
  });

  it("turns off buffering and transforms for server-sent events", () => {
    const headers = buildDownstreamHeaders(
      new Headers({ "content-type": "text/event-stream; charset=utf-8" }),
    );

    expect(headers.get("cache-control")).toBe("no-cache, no-transform");
    expect(headers.get("x-accel-buffering")).toBe("no");
  });
});
