import { HeaderNav } from "@/components/header-nav";
import { getCurrentUser } from "@/lib/auth/session";

/**
 * `HeaderNav` for the signed-in user, read from the session. Reads cookies, so it renders behind `<Suspense>`.
 * @returns the header navigation and account menu
 */
export async function SessionHeaderNav() {
  const user = await getCurrentUser();
  return <HeaderNav user={user} />;
}
