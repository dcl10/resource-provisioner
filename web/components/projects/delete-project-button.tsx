"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { toast } from "sonner";

import {
  projectQueryKey,
  projectsQueryKey,
} from "@/components/projects/use-projects";
import { DestroyPlanDialog } from "@/components/requests/destroy-plan-dialog";
import {
  destroyPlanQueryKey,
  requestsQueryKey,
} from "@/components/requests/use-requests";
import { deleteProject, type ProjectDetail } from "@/lib/projects";

/**
 * The **Delete project** button (**Retry delete** after its `project` request failed to destroy). Its dialog shows
 * everything that goes with the project, from its `project` request's destroy plan, and asks for confirmation before
 * calling `DELETE /api/projects/{id}`. The project page then follows the destroy, and leaves once the project is gone.
 * @param project - the project to delete; its status should be Succeeded, Failed or DestroyFailed
 * @returns the button and its dialog
 */
export function DeleteProjectButton({ project }: { project: ProjectDetail }) {
  const queryClient = useQueryClient();
  const [open, setOpen] = useState(false);
  const retry = project.status === "DestroyFailed";

  const remove = useMutation({
    mutationFn: () => deleteProject(project.id),
    onSuccess: (updated) => {
      setOpen(false);
      queryClient.setQueryData(projectQueryKey(project.id), updated);
      void queryClient.invalidateQueries({
        queryKey: projectsQueryKey,
        exact: true,
      });
      void queryClient.invalidateQueries({ queryKey: requestsQueryKey });
      toast.success(`Deleting project ${project.name}`, {
        description: "Everything in it is destroyed first.",
      });
    },
    onError: (error) => {
      // The plan may have changed since the dialog read it (something started, say): show the new one.
      void queryClient.invalidateQueries({
        queryKey: destroyPlanQueryKey(project.projectRequestId),
      });
      toast.error(`Couldn't delete project ${project.name}`, {
        description: error.message,
      });
    },
  });

  return (
    <DestroyPlanDialog
      requestId={project.projectRequestId}
      open={open}
      onOpenChange={setOpen}
      triggerLabel={retry ? "Retry delete" : "Delete project"}
      title={`Delete project ${project.name}?`}
      note={
        retry
          ? "The project's own destroy failed. A retry destroys it, and anything in it that's still there."
          : "Everything in the project is destroyed, then the project itself."
      }
      confirmLabel={retry ? "Retry delete" : "Delete project"}
      pending={remove.isPending}
      onConfirm={() => remove.mutate()}
    />
  );
}
