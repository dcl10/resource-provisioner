"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { Trash2 } from "lucide-react";
import { toast } from "sonner";

import {
  requestQueryKey,
  requestsQueryKey,
} from "@/components/requests/use-requests";
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
  AlertDialogTrigger,
} from "@/components/ui/alert-dialog";
import { Button } from "@/components/ui/button";
import {
  destroyRequest,
  type ProvisioningRequest,
  requestName,
} from "@/lib/requests";

/**
 * The Destroy button, asking for confirmation in an alert dialog before it queues `terraform destroy`. The request's
 * status then refreshes through TanStack Query.
 * @param request - the request to destroy; it should be Succeeded or Failed
 * @returns the button and its dialog
 */
export function DestroyButton({ request }: { request: ProvisioningRequest }) {
  const queryClient = useQueryClient();
  const name = requestName(request);
  const label = name ? `${request.displayName} "${name}"` : request.displayName;

  const destroy = useMutation({
    mutationFn: () => destroyRequest(request.id),
    onSuccess: (updated) => {
      queryClient.setQueryData(requestQueryKey(request.id), updated);
      void queryClient.invalidateQueries({ queryKey: requestsQueryKey });
      toast.success(`Destroying ${label}`, {
        description: "Follow it in the log below.",
      });
    },
    onError: (error) =>
      toast.error(`Couldn't destroy ${label}`, { description: error.message }),
  });

  return (
    <AlertDialog>
      <AlertDialogTrigger
        render={<Button variant="destructive" disabled={destroy.isPending} />}
      >
        <Trash2 />
        Destroy
      </AlertDialogTrigger>
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>Destroy {label}?</AlertDialogTitle>
          <AlertDialogDescription>
            Everything this request created in DigitalOcean is deleted,
            including any data in it. This can&apos;t be undone.
          </AlertDialogDescription>
        </AlertDialogHeader>
        <AlertDialogFooter>
          <AlertDialogCancel>Keep it</AlertDialogCancel>
          <AlertDialogAction
            variant="destructive"
            onClick={() => destroy.mutate()}
          >
            Destroy
          </AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  );
}
