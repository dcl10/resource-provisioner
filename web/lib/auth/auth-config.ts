import type { NextAuthConfig } from "next-auth";
import Keycloak from "next-auth/providers/keycloak";

import { authorizeRequest, SIGN_IN_PATH } from "./authorized";
import { updateKeycloakToken } from "./keycloak";

/**
 * Auth.js configuration. The Keycloak provider reads `AUTH_KEYCLOAK_ID`, `AUTH_KEYCLOAK_SECRET` and
 * `AUTH_KEYCLOAK_ISSUER`; Auth.js reads `AUTH_SECRET`. PKCE and state checks are on by default.
 */
export const authConfig = {
  providers: [Keycloak],
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
