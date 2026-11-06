import { describe, expect, it } from "vitest";

import { decodeJwtPayload, getRealmRoles, hasAdminRole } from "../roles";

function jwt(payload: unknown): string {
  const encode = (value: unknown) =>
    Buffer.from(JSON.stringify(value)).toString("base64url");
  return `${encode({ alg: "RS256" })}.${encode(payload)}.signature`;
}

describe("decodeJwtPayload", () => {
  it("returns the payload of a well-formed token", () => {
    expect(decodeJwtPayload(jwt({ sub: "user-1" }))).toEqual({
      sub: "user-1",
    });
  });

  it.each([
    ["undefined", undefined],
    ["an empty string", ""],
    ["a token without a payload", "header"],
    ["a payload that isn't JSON", "header.bm90IGpzb24.sig"],
    ["a payload that isn't an object", jwt(["a"])],
  ])("returns null for %s", (_, token) => {
    expect(decodeJwtPayload(token)).toBeNull();
  });
});

describe("getRealmRoles", () => {
  it("reads realm_access.roles from the access token", () => {
    const token = jwt({
      realm_access: { roles: ["rp-user", "rp-admin", "offline_access"] },
      resource_access: { account: { roles: ["manage-account"] } },
    });

    expect(getRealmRoles(token)).toEqual([
      "rp-user",
      "rp-admin",
      "offline_access",
    ]);
  });

  it("ignores roles that aren't strings", () => {
    expect(
      getRealmRoles(jwt({ realm_access: { roles: ["rp-user", 7] } })),
    ).toEqual(["rp-user"]);
  });

  it.each([
    ["no realm_access claim", jwt({ sub: "user-1" })],
    [
      "roles that aren't an array",
      jwt({ realm_access: { roles: "rp-admin" } }),
    ],
    ["an unreadable token", "not-a-jwt"],
    ["no token", undefined],
  ])("returns no roles for %s", (_, token) => {
    expect(getRealmRoles(token)).toEqual([]);
  });
});

describe("hasAdminRole", () => {
  it("is true when the roles include rp-admin", () => {
    expect(hasAdminRole(["rp-user", "rp-admin"])).toBe(true);
  });

  it.each([[["rp-user"]], [[]], [undefined], [null]])(
    "is false for %j",
    (roles) => {
      expect(hasAdminRole(roles)).toBe(false);
    },
  );
});
