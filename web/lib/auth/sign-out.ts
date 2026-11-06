/**
 * Builds Keycloak's OpenID Connect end-session URL, which ends the Keycloak session and sends the browser back to
 * the app.
 * @param options - the realm's issuer URL, the client ID, an ID token for `id_token_hint` and where to return to
 * @returns the logout URL, or null when no issuer is configured
 */
export function buildKeycloakLogoutUrl({
  issuer,
  clientId,
  idTokenHint,
  postLogoutRedirectUri,
}: {
  issuer?: string;
  clientId?: string;
  idTokenHint?: string;
  postLogoutRedirectUri?: string;
}): URL | null {
  if (!issuer) return null;

  const logoutUrl = new URL(
    "protocol/openid-connect/logout",
    issuer.endsWith("/") ? issuer : `${issuer}/`,
  );

  if (idTokenHint) logoutUrl.searchParams.set("id_token_hint", idTokenHint);
  if (clientId) logoutUrl.searchParams.set("client_id", clientId);
  if (postLogoutRedirectUri) {
    logoutUrl.searchParams.set(
      "post_logout_redirect_uri",
      postLogoutRedirectUri,
    );
  }

  return logoutUrl;
}
