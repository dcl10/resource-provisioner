import { RequestsTable } from "@/components/requests/requests-table";
import { getCurrentUser } from "@/lib/auth/session";

/**
 * The requests table for the signed-in user, filtered to one project when the URL has `?project=<id>`. Reads the
 * session to know whether to show the owner column, and the URL's query, so it has to sit behind `<Suspense>`.
 * @param searchParams - the page's query
 * @returns the table
 */
export async function RequestsList({
  searchParams,
}: {
  searchParams: Promise<Record<string, string | string[] | undefined>>;
}) {
  const [user, query] = await Promise.all([getCurrentUser(), searchParams]);
  const project = typeof query.project === "string" ? query.project : null;
  return <RequestsTable isAdmin={user?.isAdmin ?? false} projectId={project} />;
}
