import { customFetch, type NextAuthConfig } from "next-auth";
import Keycloak from "next-auth/providers/keycloak";

import { authorizeRequest, SIGN_IN_PATH } from "./authorized";
import { updateKeycloakToken } from "./keycloak";
import { keycloakFetch } from "./keycloak-url";

/**
 * Auth.js configuration. The Keycloak provider reads `AUTH_KEYCLOAK_ID`, `AUTH_KEYCLOAK_SECRET` and
 * `AUTH_KEYCLOAK_ISSUER`; Auth.js reads `AUTH_SECRET`. PKCE and state checks are on by default. Server-side calls to
 * Keycloak go through `keycloakFetch`, which uses `AUTH_KEYCLOAK_INTERNAL_ISSUER` when set (Docker Compose).
 */
export const authConfig = {
  providers: [Keycloak({ [customFetch]: keycloakFetch })],
  session: { strategy: "jwt" },
  callbacks: {
    authorized: authorizeRequest,
    jwt({ token, account }) {
      return updateKeycloakToken(token, account);
    },
    session({ session, token }) {
      return {
        ...session,
        user: { ...session.user, id: token.sub ?? "" },
        roles: token.roles ?? [],
        accessToken: token.accessToken,
        idToken: token.idToken,
        error: token.error,
      };
    },
  },
  pages: {
    signIn: SIGN_IN_PATH,
    error: SIGN_IN_PATH,
  },
} satisfies NextAuthConfig;
