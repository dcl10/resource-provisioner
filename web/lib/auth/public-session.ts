/** What `/api/auth/session` may show the browser. */
export interface PublicSession {
  user: { name: string | null; email: string | null };
  expires: string | null;
  error?: string;
}

function stringOrNull(value: unknown): string | null {
  return typeof value === "string" ? value : null;
}

/**
 * Cuts a session down to the fields the browser may see. Server code gets the Keycloak tokens from `auth()`, but
 * Auth.js's `/api/auth/session` endpoint runs the same callback, so its response goes through this first.
 * @param session - the session JSON Auth.js produced, or null when signed out
 * @returns the user's name and email, the expiry and any error; null when there's no session
 */
export function toPublicSession(session: unknown): PublicSession | null {
  if (!session || typeof session !== "object" || !("user" in session)) {
    return null;
  }

  const { user, expires, error } = session as {
    user?: { name?: unknown; email?: unknown } | null;
    expires?: unknown;
    error?: unknown;
  };

  return {
    user: { name: stringOrNull(user?.name), email: stringOrNull(user?.email) },
    expires: stringOrNull(expires),
    ...(typeof error === "string" ? { error } : {}),
  };
}
