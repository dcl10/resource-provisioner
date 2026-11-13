"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { toast } from "sonner";

import { projectsQueryKey } from "@/components/projects/use-projects";
import { DestroyPlanDialog } from "@/components/requests/destroy-plan-dialog";
import {
  destroyPlanQueryKey,
  requestQueryKey,
  requestsQueryKey,
} from "@/components/requests/use-requests";
import {
  destroyRequest,
  type ProvisioningRequest,
  requestName,
} from "@/lib/requests";

/**
 * The Destroy button (**Retry destroy** after a failed destroy). Its dialog shows everything the destroy takes with it,
 * from the request's destroy plan, and asks for confirmation before it queues the cascading destroy. The request's
 * status then refreshes through TanStack Query.
 * @param request - the request to destroy; it should be Succeeded, Failed or DestroyFailed
 * @returns the button and its dialog
 */
export function DestroyButton({ request }: { request: ProvisioningRequest }) {
  const queryClient = useQueryClient();
  const [open, setOpen] = useState(false);
  const name = requestName(request);
  const label = name ? `${request.displayName} "${name}"` : request.displayName;
  const retry = request.status === "DestroyFailed";

  const destroy = useMutation({
    mutationFn: () => destroyRequest(request.id),
    onSuccess: (updated) => {
      setOpen(false);
      queryClient.setQueryData(requestQueryKey(request.id), updated);
      void queryClient.invalidateQueries({ queryKey: requestsQueryKey });
      void queryClient.invalidateQueries({ queryKey: projectsQueryKey });
      toast.success(`Destroying ${label}`, {
        description: "Follow it in the log below.",
      });
    },
    onError: (error) => {
      // The plan may have changed since the dialog read it (something started, say): show the new one.
      void queryClient.invalidateQueries({
        queryKey: destroyPlanQueryKey(request.id),
      });
      toast.error(`Couldn't destroy ${label}`, { description: error.message });
    },
  });

  return (
    <DestroyPlanDialog
      requestId={request.id}
      open={open}
      onOpenChange={setOpen}
      triggerLabel={retry ? "Retry destroy" : "Destroy"}
      title={retry ? `Retry destroying ${label}?` : `Destroy ${label}?`}
      note={
        retry
          ? "A retry destroys only this and what depends on it. Anything whose destroy stopped because this one failed keeps its status: destroy it again from its own page."
          : undefined
      }
      confirmLabel={retry ? "Retry destroy" : "Destroy"}
      pending={destroy.isPending}
      onConfirm={() => destroy.mutate()}
    />
  );
}
