/**
 * Points a server-side call to Keycloak at the address this server can reach. In Docker Compose the browser reaches
 * Keycloak at `localhost:8080` but the web container can't, so with `AUTH_KEYCLOAK_INTERNAL_ISSUER` set, a URL on the
 * public issuer's origin gets the internal origin instead. Tokens still name the public issuer, so nothing that's
 * checked changes. Without the variable (local `npm run dev`, or deployed) URLs are left alone.
 * @param url - a URL the server is about to call
 * @param env - where to read `AUTH_KEYCLOAK_ISSUER` (the public issuer) and `AUTH_KEYCLOAK_INTERNAL_ISSUER` (the same
 * realm as this server reaches it); `process.env` by default
 * @returns the URL to call
 */
export function toInternalKeycloakUrl(
  url: string | URL,
  env: Record<string, string | undefined> = process.env,
): string {
  const original = url.toString();
  const {
    AUTH_KEYCLOAK_ISSUER: publicIssuer,
    AUTH_KEYCLOAK_INTERNAL_ISSUER: internalIssuer,
  } = env;
  if (!publicIssuer || !internalIssuer) return original;

  const target = new URL(original);
  if (target.origin !== new URL(publicIssuer).origin) return original;

  const internal = new URL(internalIssuer);
  target.protocol = internal.protocol;
  target.host = internal.host;
  return target.toString();
}

/**
 * `fetch` for Auth.js's Keycloak provider (its `[customFetch]` option): discovery, the code exchange and user info
 * go to the internal address when there is one.
 * @param args - fetch's arguments
 * @returns fetch's response
 */
export async function keycloakFetch(
  ...args: Parameters<typeof fetch>
): Promise<Response> {
  const [input, init] = args;
  if (!(input instanceof Request)) {
    return fetch(toInternalKeycloakUrl(input), init);
  }

  // A Request can't serve as the init of another, so copy what Auth.js sets on it.
  return fetch(toInternalKeycloakUrl(input.url), {
    method: input.method,
    headers: input.headers,
    body: input.body ? await input.arrayBuffer() : undefined,
    redirect: input.redirect,
    signal: input.signal,
    ...init,
  });
}
