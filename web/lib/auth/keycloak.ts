import type { Account } from "next-auth";
import type { JWT } from "next-auth/jwt";

import { getRealmRoles } from "./roles";

/** Refresh this many seconds before the access token expires, so it doesn't lapse on its way to the API. */
const REFRESH_MARGIN_SECONDS = 30;

type TokenResponse = {
  access_token?: unknown;
  expires_in?: unknown;
  refresh_token?: unknown;
  id_token?: unknown;
};

function nowInSeconds(): number {
  return Math.floor(Date.now() / 1000);
}

/**
 * Swaps the refresh token for a new access token at Keycloak's token endpoint.
 * @param token - the session JWT holding the refresh token
 * @returns the JWT with new tokens and roles, or with `error` set and the tokens removed when Keycloak refuses
 */
export async function refreshKeycloakAccessToken(token: JWT): Promise<JWT> {
  try {
    if (!token.refreshToken) throw new Error("Missing refresh token");

    const response = await fetch(
      `${process.env.AUTH_KEYCLOAK_ISSUER}/protocol/openid-connect/token`,
      {
        method: "POST",
        headers: { "Content-Type": "application/x-www-form-urlencoded" },
        body: new URLSearchParams({
          client_id: process.env.AUTH_KEYCLOAK_ID ?? "",
          client_secret: process.env.AUTH_KEYCLOAK_SECRET ?? "",
          grant_type: "refresh_token",
          refresh_token: token.refreshToken,
        }),
        cache: "no-store",
      },
    );
    const refreshed = (await response.json()) as TokenResponse;

    if (!response.ok) throw new Error(JSON.stringify(refreshed));
    if (
      typeof refreshed.access_token !== "string" ||
      typeof refreshed.expires_in !== "number"
    ) {
      throw new Error("Keycloak returned an invalid token response");
    }

    return {
      ...token,
      accessToken: refreshed.access_token,
      expiresAt: nowInSeconds() + refreshed.expires_in,
      // Keycloak may rotate the refresh token; keep the old one if it doesn't.
      refreshToken:
        typeof refreshed.refresh_token === "string"
          ? refreshed.refresh_token
          : token.refreshToken,
      idToken:
        typeof refreshed.id_token === "string"
          ? refreshed.id_token
          : token.idToken,
      roles: getRealmRoles(refreshed.access_token),
      error: undefined,
    };
  } catch (error) {
    console.error("Refreshing the Keycloak access token failed", error);

    return {
      ...token,
      accessToken: undefined,
      refreshToken: undefined,
      roles: [],
      error: "RefreshAccessTokenError",
    };
  }
}

/**
 * The Auth.js `jwt` callback: stores Keycloak's tokens and the user's realm roles at sign-in, and refreshes the
 * access token once it's about to expire.
 * @param token - the current session JWT
 * @param account - the provider account, present only on the sign-in that created the session
 * @returns the JWT to store in the session cookie
 */
export async function updateKeycloakToken(
  token: JWT,
  account?: Account | null,
): Promise<JWT> {
  if (account) {
    return {
      ...token,
      accessToken: account.access_token,
      idToken: account.id_token,
      refreshToken: account.refresh_token,
      expiresAt: account.expires_at,
      roles: getRealmRoles(account.access_token),
      error: undefined,
    };
  }

  if (token.error) return token;

  const expiresAt = token.expiresAt ?? 0;
  return nowInSeconds() < expiresAt - REFRESH_MARGIN_SECONDS
    ? token
    : refreshKeycloakAccessToken(token);
}
