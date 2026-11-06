"use server";

import { headers } from "next/headers";
import { redirect } from "next/navigation";

import { auth, signIn, signOut } from "@/auth";

import { SIGN_IN_PATH } from "./authorized";
import { toSafeCallbackPath } from "./callback-url";
import { buildKeycloakLogoutUrl } from "./sign-out";

async function getAppOrigin(): Promise<string> {
  if (process.env.AUTH_URL) return new URL(process.env.AUTH_URL).origin;

  const requestHeaders = await headers();
  const host =
    requestHeaders.get("x-forwarded-host") ?? requestHeaders.get("host");
  const protocol = requestHeaders.get("x-forwarded-proto") ?? "http";
  return `${protocol}://${host}`;
}

/**
 * Starts the Keycloak sign-in flow.
 * @param callbackUrl - where to go afterwards; anything off this site falls back to the chat page
 */
export async function signInWithKeycloak(callbackUrl?: string): Promise<void> {
  const redirectTo = toSafeCallbackPath(callbackUrl, await getAppOrigin());
  await signIn("keycloak", { redirectTo });
}

/**
 * Federated sign-out: clears the Auth.js session cookie, then sends the browser to Keycloak's end-session endpoint,
 * which ends the Keycloak session too and returns to the app. Without the second step the next sign-in would skip
 * the Keycloak login form.
 */
export async function signOutEverywhere(): Promise<void> {
  const session = await auth();
  const logoutUrl = buildKeycloakLogoutUrl({
    issuer: process.env.AUTH_KEYCLOAK_ISSUER,
    clientId: process.env.AUTH_KEYCLOAK_ID,
    idTokenHint: session?.idToken,
    postLogoutRedirectUri: `${await getAppOrigin()}/`,
  });

  await signOut({ redirect: false });
  redirect(logoutUrl?.toString() ?? SIGN_IN_PATH);
}
