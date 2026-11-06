import { API_PROXY_PREFIX } from "@/lib/auth/authorized";

/** Request headers passed on to the API. Cookies stay behind: the API only accepts the bearer token. */
const FORWARDED_REQUEST_HEADERS = [
  "accept",
  "accept-language",
  "content-type",
  "last-event-id",
];

/**
 * Response headers not copied back to the browser: hop-by-hop headers, and the encoding and length headers that no
 * longer describe the body once `fetch` has decompressed it.
 */
const DROPPED_RESPONSE_HEADERS = new Set([
  "connection",
  "keep-alive",
  "transfer-encoding",
  "content-encoding",
  "content-length",
  "set-cookie",
]);

/**
 * Maps a proxy URL onto the API: `/api/proxy/me?x=1` becomes `<apiBaseUrl>/api/me?x=1`.
 * @param apiBaseUrl - the API's base URL, e.g. `http://localhost:5000`
 * @param pathname - the incoming request path, starting with `/api/proxy`
 * @param search - the incoming query string, including `?`, or empty
 * @returns the API URL, or null when the path would leave the API's `/api/` prefix
 */
export function buildApiUrl(
  apiBaseUrl: string,
  pathname: string,
  search: string,
): URL | null {
  if (!pathname.startsWith(`${API_PROXY_PREFIX}/`)) return null;

  const base = new URL(apiBaseUrl);
  const url = new URL(
    `/api/${pathname.slice(API_PROXY_PREFIX.length + 1)}${search}`,
    base,
  );

  return url.origin === base.origin && url.pathname.startsWith("/api/")
    ? url
    : null;
}

/**
 * Builds the headers for the request to the API: the allowed browser headers plus the bearer token.
 * @param incoming - the browser's request headers
 * @param accessToken - the Keycloak access token from the session
 * @returns the headers to send upstream
 */
export function buildUpstreamHeaders(
  incoming: Headers,
  accessToken: string,
): Headers {
  const upstream = new Headers();
  for (const name of FORWARDED_REQUEST_HEADERS) {
    const value = incoming.get(name);
    if (value !== null) upstream.set(name, value);
  }
  upstream.set("authorization", `Bearer ${accessToken}`);
  return upstream;
}

/**
 * Copies the API's response headers for the browser, minus the ones that would be wrong or unsafe to pass on.
 * Server-sent events get `X-Accel-Buffering: no` so a reverse proxy in front of the app doesn't hold them back.
 * @param upstream - the API's response headers
 * @returns the headers for the response to the browser
 */
export function buildDownstreamHeaders(upstream: Headers): Headers {
  const downstream = new Headers();
  upstream.forEach((value, name) => {
    if (!DROPPED_RESPONSE_HEADERS.has(name.toLowerCase())) {
      downstream.set(name, value);
    }
  });
  if (downstream.get("content-type")?.startsWith("text/event-stream")) {
    downstream.set("cache-control", "no-cache, no-transform");
    downstream.set("x-accel-buffering", "no");
  }
  return downstream;
}
