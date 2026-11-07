import { RequestsTable } from "@/components/requests/requests-table";
import { getCurrentUser } from "@/lib/auth/session";

/**
 * The requests table for the signed-in user. Reads the session to know whether to show the owner column, so it has
 * to sit behind `<Suspense>`.
 * @returns the table
 */
export async function RequestsList() {
  const user = await getCurrentUser();
  return <RequestsTable isAdmin={user?.isAdmin ?? false} />;
}
