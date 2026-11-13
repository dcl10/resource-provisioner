import { redirect } from "next/navigation";
import { Suspense } from "react";

import {
  ProjectDetail,
  ProjectDetailSkeleton,
} from "@/components/projects/project-detail";
import { getCurrentUser } from "@/lib/auth/session";

export default function ProjectPage({ params }: PageProps<"/projects/[id]">) {
  return (
    <Suspense fallback={<ProjectDetailSkeleton />}>
      <ProjectForUser params={params} />
    </Suspense>
  );
}

/** Waits for the route's id and the session, both only known per request. */
async function ProjectForUser({ params }: { params: Promise<{ id: string }> }) {
  const [{ id }, user] = await Promise.all([params, getCurrentUser()]);
  if (!user) redirect("/sign-in");

  return (
    <ProjectDetail
      key={id}
      projectId={id}
      user={{ id: user.id, isAdmin: user.isAdmin }}
    />
  );
}
