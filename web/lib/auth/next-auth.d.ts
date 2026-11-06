import type { DefaultSession } from "next-auth";

import type { AuthError } from "./types";

declare module "next-auth" {
  /**
   * The session as server code sees it through `auth()`. The tokens never reach the browser: `/api/auth/session`
   * returns only what `toPublicSession` keeps.
   */
  interface Session {
    user: { id: string } & DefaultSession["user"];
    roles?: string[];
    accessToken?: string;
    idToken?: string;
    error?: AuthError;
  }
}

declare module "next-auth/jwt" {
  /** The encrypted session cookie's contents. */
  interface JWT {
    accessToken?: string;
    idToken?: string;
    refreshToken?: string;
    /** Access token expiry, in seconds since the epoch. */
    expiresAt?: number;
    roles?: string[];
    error?: AuthError;
  }
}
