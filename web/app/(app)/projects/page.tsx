import { Suspense } from "react";

import { NewProjectDialog } from "@/components/projects/new-project-dialog";
import { ProjectsList } from "@/components/projects/projects-list";
import { RequestsTableSkeleton } from "@/components/requests/requests-table";

export default function ProjectsPage() {
  return (
    <div className="flex flex-col gap-6">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <h1 className="text-2xl font-semibold tracking-tight">Projects</h1>
        <NewProjectDialog />
      </div>
      <Suspense fallback={<RequestsTableSkeleton />}>
        <ProjectsList />
      </Suspense>
    </div>
  );
}
