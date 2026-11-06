import { describe, expect, it } from "vitest";

import { toPublicSession } from "../public-session";

describe("toPublicSession", () => {
  it("keeps the user's name and email and the expiry, and drops the tokens", () => {
    const session = {
      user: {
        id: "user-1",
        name: "Ada",
        email: "ada@example.com",
        image: null,
      },
      expires: "2026-12-01T00:00:00.000Z",
      roles: ["rp-admin"],
      accessToken: "access",
      idToken: "id",
    };

    expect(toPublicSession(session)).toEqual({
      user: { name: "Ada", email: "ada@example.com" },
      expires: "2026-12-01T00:00:00.000Z",
    });
  });

  it("keeps a refresh error so the client can tell the session is dead", () => {
    expect(
      toPublicSession({ user: {}, error: "RefreshAccessTokenError" }),
    ).toEqual({
      user: { name: null, email: null },
      expires: null,
      error: "RefreshAccessTokenError",
    });
  });

  it.each([[null], [{}], ["session"]])("returns null for %j", (session) => {
    expect(toPublicSession(session)).toBeNull();
  });
});
