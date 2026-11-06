/** The signed-in user, as much of them as the UI needs. Safe to pass to Client Components. */
export interface SessionUser {
  /** Keycloak user ID (`sub`), which the API owns data by. */
  id: string;
  name: string | null;
  email: string | null;
  /** Whether the user has the `rp-admin` realm role. */
  isAdmin: boolean;
}

/** Set on the session when Keycloak refused to refresh the access token; the user has to sign in again. */
export type AuthError = "RefreshAccessTokenError";
