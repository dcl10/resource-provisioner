import "server-only";

import { cache } from "react";

import { auth } from "@/auth";

import { isSignedIn } from "./authorized";
import { hasAdminRole } from "./roles";
import type { SessionUser } from "./types";

/**
 * The signed-in user, read once per request. Reads cookies, so with Cache Components the calling component has to
 * sit behind a `<Suspense>` boundary.
 * @returns the user, or null when nobody is signed in or the session can't be refreshed
 */
export const getCurrentUser = cache(async (): Promise<SessionUser | null> => {
  const session = await auth();
  if (!session || !isSignedIn(session)) return null;

  return {
    id: session.user.id,
    name: session.user.name ?? null,
    email: session.user.email ?? null,
    isAdmin: hasAdminRole(session.roles),
  };
});
