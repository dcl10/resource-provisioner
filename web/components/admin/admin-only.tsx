import { redirect } from "next/navigation";
import type { ReactNode } from "react";

import { getCurrentUser } from "@/lib/auth/session";

/**
 * Renders its children only for admins and sends everyone else to the chat. Reads the session, so it has to sit
 * behind `<Suspense>`. It only keeps non-admins off admin pages; the API checks `rp-admin` on every admin call.
 * @param children - the admin-only content
 * @returns the children, for an admin
 */
export async function AdminOnly({ children }: { children: ReactNode }) {
  const user = await getCurrentUser();
  if (!user?.isAdmin) redirect("/chat");

  return children;
}
