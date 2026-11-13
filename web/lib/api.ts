/** Base path of the .NET API, through the Next.js proxy that adds the access token. */
export const API_BASE = "/api/proxy";

/**
 * An error response from the API, with a message fit to show the user.
 */
export class ApiError extends Error {
  constructor(
    message: string,
    readonly status: number,
    /** The problem's `title`, which some errors use as a category (such as `Active request limit reached`). */
    readonly title?: string,
  ) {
    super(message);
    this.name = "ApiError";
  }
}

/** The fields of an RFC 9457 problem details body that the API sends. */
interface Problem {
  detail?: string;
  title?: string;
  errors?: Record<string, string[]>;
}

/**
 * Reads an API error response into an {@link ApiError}. The API sends problem details: `detail` for most errors and
 * `errors` (field to messages) for validation failures.
 * @param response - the failed response
 * @returns the error, with a message to show the user
 */
export async function readApiError(response: Response): Promise<ApiError> {
  let problem: Problem = {};
  try {
    problem = (await response.json()) as Problem;
  } catch {
    // Not JSON (a proxy error page, an empty body): fall back to the status.
  }

  const firstError = problem.errors
    ? Object.values(problem.errors).flat()[0]
    : undefined;
  const message =
    problem.detail ??
    firstError ??
    problem.title ??
    `Request failed (${response.status}).`;
  return new ApiError(message, response.status, problem.title);
}

/**
 * Calls the API through the proxy and reads a JSON response.
 * @param path - the path after `/api/`, such as `/conversations`
 * @param init - fetch options
 * @returns the parsed body; undefined for a 204 No Content
 * @throws ApiError when the response isn't successful
 */
export async function apiFetch<T>(
  path: string,
  init?: RequestInit,
): Promise<T> {
  const response = await fetch(`${API_BASE}${path}`, {
    ...init,
    headers: { Accept: "application/json", ...init?.headers },
  });
  if (!response.ok) {
    throw await readApiError(response);
  }
  if (response.status === 204) return undefined as T;
  return (await response.json()) as T;
}

/**
 * Whether an error is the API's 404: what was asked for doesn't exist (a cancelled or destroyed request is deleted),
 * or isn't the user's.
 * @param error - the error, if any
 * @returns whether it's a 404
 */
export function isNotFound(error: unknown): boolean {
  return error instanceof ApiError && error.status === 404;
}

/**
 * A TanStack Query `retry` rule: retry network and server errors up to three times, but never a 4xx, which won't
 * change on retry (a request or conversation that doesn't exist, or isn't the user's).
 * @param failureCount - how many times the query has failed
 * @param error - the latest error
 * @returns whether to try again
 */
export function retryUnlessClientError(
  failureCount: number,
  error: Error,
): boolean {
  const clientError =
    error instanceof ApiError && error.status >= 400 && error.status < 500;
  return !clientError && failureCount < 3;
}
