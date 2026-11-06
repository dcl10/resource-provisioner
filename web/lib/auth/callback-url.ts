/** Where signing in lands when there's no usable callback URL. */
export const DEFAULT_CALLBACK_PATH = "/chat";

/**
 * Turns a `callbackUrl` search parameter into a same-origin path, so signing in can't redirect off the site.
 * @param callbackUrl - the raw parameter: a path, or an absolute URL on this app's origin
 * @param origin - this app's origin, e.g. `http://localhost:3000`
 * @returns the path (with search and hash) to go to after signing in
 */
export function toSafeCallbackPath(
  callbackUrl: string | string[] | undefined,
  origin: string,
): string {
  if (typeof callbackUrl !== "string" || callbackUrl.length === 0) {
    return DEFAULT_CALLBACK_PATH;
  }

  try {
    const url = new URL(callbackUrl, origin);
    if (url.origin !== new URL(origin).origin) return DEFAULT_CALLBACK_PATH;
    if (url.pathname === "/" || url.pathname.startsWith("/sign-in")) {
      return DEFAULT_CALLBACK_PATH;
    }

    return `${url.pathname}${url.search}${url.hash}`;
  } catch {
    return DEFAULT_CALLBACK_PATH;
  }
}
