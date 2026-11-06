import type { Account } from "next-auth";
import type { JWT } from "next-auth/jwt";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { refreshKeycloakAccessToken, updateKeycloakToken } from "../keycloak";

const NOW = 1_800_000_000;

function jwt(payload: unknown): string {
  const encode = (value: unknown) =>
    Buffer.from(JSON.stringify(value)).toString("base64url");
  return `${encode({ alg: "RS256" })}.${encode(payload)}.signature`;
}

const adminAccessToken = jwt({
  realm_access: { roles: ["rp-user", "rp-admin"] },
});
const userAccessToken = jwt({ realm_access: { roles: ["rp-user"] } });

const signedIn: JWT = {
  sub: "user-1",
  accessToken: userAccessToken,
  idToken: "id-1",
  refreshToken: "refresh-1",
  expiresAt: NOW + 300,
  roles: ["rp-user"],
};

const fetchMock = vi.fn<typeof fetch>();

beforeEach(() => {
  vi.useFakeTimers();
  vi.setSystemTime(NOW * 1000);
  vi.stubGlobal("fetch", fetchMock);
  vi.stubEnv(
    "AUTH_KEYCLOAK_ISSUER",
    "http://localhost:8080/realms/resource-provisioner",
  );
  vi.stubEnv("AUTH_KEYCLOAK_ID", "rp-web");
  vi.stubEnv("AUTH_KEYCLOAK_SECRET", "web-secret");
  vi.spyOn(console, "error").mockImplementation(() => {});
});

afterEach(() => {
  vi.useRealTimers();
  vi.unstubAllGlobals();
  vi.unstubAllEnvs();
  vi.restoreAllMocks();
  fetchMock.mockReset();
});

describe("updateKeycloakToken", () => {
  it("stores the tokens and realm roles at sign-in", async () => {
    const account = {
      provider: "keycloak",
      type: "oidc",
      providerAccountId: "user-1",
      access_token: adminAccessToken,
      id_token: "id-1",
      refresh_token: "refresh-1",
      expires_at: NOW + 300,
    } satisfies Account;

    const token = await updateKeycloakToken({ sub: "user-1" }, account);

    expect(token).toEqual({
      sub: "user-1",
      accessToken: adminAccessToken,
      idToken: "id-1",
      refreshToken: "refresh-1",
      expiresAt: NOW + 300,
      roles: ["rp-user", "rp-admin"],
      error: undefined,
    });
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("keeps a token that isn't close to expiring", async () => {
    expect(await updateKeycloakToken(signedIn)).toBe(signedIn);
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("refreshes a token within 30 seconds of expiring", async () => {
    fetchMock.mockResolvedValue(
      Response.json({ access_token: adminAccessToken, expires_in: 300 }),
    );

    const token = await updateKeycloakToken({
      ...signedIn,
      expiresAt: NOW + 10,
    });

    expect(fetchMock).toHaveBeenCalledOnce();
    expect(token.accessToken).toBe(adminAccessToken);
    expect(token.roles).toEqual(["rp-user", "rp-admin"]);
  });

  it("doesn't retry once a refresh has failed", async () => {
    const failed: JWT = { ...signedIn, error: "RefreshAccessTokenError" };

    expect(
      await updateKeycloakToken({ ...failed, expiresAt: NOW - 1 }),
    ).toEqual({ ...failed, expiresAt: NOW - 1 });
    expect(fetchMock).not.toHaveBeenCalled();
  });
});

describe("refreshKeycloakAccessToken", () => {
  it("posts the refresh token to the realm's token endpoint", async () => {
    fetchMock.mockResolvedValue(
      Response.json({ access_token: userAccessToken, expires_in: 300 }),
    );

    await refreshKeycloakAccessToken(signedIn);

    const [url, init] = fetchMock.mock.calls[0];
    expect(url).toBe(
      "http://localhost:8080/realms/resource-provisioner/protocol/openid-connect/token",
    );
    expect(init?.method).toBe("POST");
    expect(Object.fromEntries(init?.body as URLSearchParams)).toEqual({
      client_id: "rp-web",
      client_secret: "web-secret",
      grant_type: "refresh_token",
      refresh_token: "refresh-1",
    });
  });

  it("takes the new tokens, expiry and roles", async () => {
    fetchMock.mockResolvedValue(
      Response.json({
        access_token: adminAccessToken,
        expires_in: 300,
        refresh_token: "refresh-2",
        id_token: "id-2",
      }),
    );

    expect(await refreshKeycloakAccessToken(signedIn)).toEqual({
      ...signedIn,
      accessToken: adminAccessToken,
      idToken: "id-2",
      refreshToken: "refresh-2",
      expiresAt: NOW + 300,
      roles: ["rp-user", "rp-admin"],
      error: undefined,
    });
  });

  it("keeps the old refresh and ID tokens when Keycloak doesn't send new ones", async () => {
    fetchMock.mockResolvedValue(
      Response.json({ access_token: userAccessToken, expires_in: 300 }),
    );

    const token = await refreshKeycloakAccessToken(signedIn);

    expect(token.refreshToken).toBe("refresh-1");
    expect(token.idToken).toBe("id-1");
  });

  it.each([
    [
      "Keycloak rejects the refresh token",
      () =>
        Response.json({ error: "invalid_grant" }, { status: 400 }) as Response,
    ],
    [
      "the response has no access token",
      () => Response.json({ expires_in: 300 }),
    ],
  ])("marks the session failed when %s", async (_, response) => {
    fetchMock.mockResolvedValue(response());

    const token = await refreshKeycloakAccessToken(signedIn);

    expect(token.error).toBe("RefreshAccessTokenError");
    expect(token.accessToken).toBeUndefined();
    expect(token.refreshToken).toBeUndefined();
    expect(token.roles).toEqual([]);
  });

  it("marks the session failed without calling Keycloak when there's no refresh token", async () => {
    const token = await refreshKeycloakAccessToken({
      ...signedIn,
      refreshToken: undefined,
    });

    expect(token.error).toBe("RefreshAccessTokenError");
    expect(fetchMock).not.toHaveBeenCalled();
  });
});
