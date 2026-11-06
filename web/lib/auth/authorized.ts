import type { Session } from "next-auth";

/** The sign-in page, the one page reachable without a session. */
export const SIGN_IN_PATH = "/sign-in";

/** Route handlers under this prefix forward to the API. */
export const API_PROXY_PREFIX = "/api/proxy";

/**
 * Whether a session belongs to a signed-in user whose tokens are still good.
 * @param session - the session from `auth()`, or null
 * @returns true when the user is signed in and the last token refresh didn't fail
 */
export function isSignedIn(session: Session | null | undefined): boolean {
  return !!session?.user && !session.error;
}

/**
 * The Auth.js `authorized` callback, run by `proxy.ts` on every page and proxy request. Pages need a session and
 * redirect to the sign-in page without one (Auth.js adds `callbackUrl`); the API proxy answers 401 instead.
 * @param params - the session (null when signed out) and the request
 * @returns true to let the request through, false to redirect to sign-in, or a 401 response for the API proxy
 */
export function authorizeRequest({
  auth,
  request,
}: {
  auth: Session | null;
  request: { nextUrl: URL };
}): boolean | Response {
  const { pathname } = request.nextUrl;

  if (pathname === SIGN_IN_PATH) return true;
  if (isSignedIn(auth)) return true;

  if (
    pathname === API_PROXY_PREFIX ||
    pathname.startsWith(`${API_PROXY_PREFIX}/`)
  ) {
    return Response.json({ error: "Not signed in" }, { status: 401 });
  }

  return false;
}
