import { ProjectsTable } from "@/components/projects/projects-table";
import { getCurrentUser } from "@/lib/auth/session";

/**
 * The projects table for the signed-in user. Reads the session to know whether to show the owner column, so it has
 * to sit behind `<Suspense>`.
 * @returns the table
 */
export async function ProjectsList() {
  const user = await getCurrentUser();
  return <ProjectsTable isAdmin={user?.isAdmin ?? false} />;
}
