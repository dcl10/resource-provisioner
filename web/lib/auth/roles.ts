/** The Keycloak realm role that grants admin pages and every user's data. */
export const ADMIN_ROLE = "rp-admin";

type AccessTokenClaims = {
  realm_access?: { roles?: unknown };
};

/**
 * Decodes a JWT's payload without checking its signature. Only use it on tokens that came straight from Keycloak
 * over the back channel; the API is what validates them.
 * @param jwt - the encoded token
 * @returns the payload, or null when the token isn't a well-formed JWT
 */
export function decodeJwtPayload(
  jwt?: string | null,
): Record<string, unknown> | null {
  const payload = jwt?.split(".")[1];
  if (!payload) return null;

  try {
    const claims: unknown = JSON.parse(
      Buffer.from(payload, "base64url").toString("utf-8"),
    );
    return claims && typeof claims === "object" && !Array.isArray(claims)
      ? (claims as Record<string, unknown>)
      : null;
  } catch {
    return null;
  }
}

/**
 * Reads the realm roles (`realm_access.roles`) from a Keycloak access token.
 * @param accessToken - the encoded access token
 * @returns the role names, or an empty array when there are none or the token can't be read
 */
export function getRealmRoles(accessToken?: string | null): string[] {
  const claims = decodeJwtPayload(accessToken) as AccessTokenClaims | null;
  const roles = claims?.realm_access?.roles;
  return Array.isArray(roles)
    ? roles.filter((role): role is string => typeof role === "string")
    : [];
}

/**
 * Whether a set of realm roles includes the admin role.
 * @param roles - realm role names
 * @returns true when the user is an admin
 */
export function hasAdminRole(roles?: readonly string[] | null): boolean {
  return roles?.includes(ADMIN_ROLE) ?? false;
}
