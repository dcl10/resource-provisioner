"use client";

import { Trash2 } from "lucide-react";
import type { ReactNode } from "react";

import { StatusBadge } from "@/components/requests/status-badge";
import { useDestroyPlan } from "@/components/requests/use-requests";
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
import { Skeleton } from "@/components/ui/skeleton";
import { linkLabel, type RequestLink } from "@/lib/requests";
import { cn } from "@/lib/utils";

/** Props for {@link DestroyPlanDialog}. */
export interface DestroyPlanDialogProps {
  /** The request whose destroy plan to show: the request itself, or a project's `project` request. */
  requestId: string;
  /** Whether the dialog is open; the caller closes it once its action has succeeded. */
  open: boolean;
  /** Called when the dialog opens or closes. */
  onOpenChange: (open: boolean) => void;
  /** The trigger button's text, such as `Destroy`. */
  triggerLabel: string;
  /** The dialog's title, such as `Destroy Droplet "web-1"?`. */
  title: string;
  /** Anything to say before the plan, such as what a retry does. */
  note?: ReactNode;
  /** The confirming button's text. */
  confirmLabel: string;
  /** Whether the action is running: both buttons are disabled meanwhile. */
  pending: boolean;
  /** Runs the destroy (or delete) once the user confirms. */
  onConfirm: () => void;
}

/**
 * A destructive button whose alert dialog shows what it would tear down before anything is: the request's destroy plan
 * (`GET /api/requests/{id}/destroy-plan`), read afresh each time it opens. It lists what's destroyed, in teardown order
 * (the request itself last), and the proposals that are cancelled; or, when a destroy would be refused now, the API's
 * reason and what's still running, with the confirming button disabled until it can go ahead.
 * @param props - see {@link DestroyPlanDialogProps}
 * @returns the button and its dialog
 */
export function DestroyPlanDialog({
  requestId,
  open,
  onOpenChange,
  triggerLabel,
  title,
  note,
  confirmLabel,
  pending,
  onConfirm,
}: DestroyPlanDialogProps) {
  return (
    <AlertDialog open={open} onOpenChange={onOpenChange}>
      <AlertDialogTrigger
        render={<Button variant="destructive" disabled={pending} />}
      >
        <Trash2 />
        {triggerLabel}
      </AlertDialogTrigger>
      <AlertDialogContent className="sm:max-w-md">
        {open ? (
          <PlanContent
            requestId={requestId}
            title={title}
            note={note}
            confirmLabel={confirmLabel}
            pending={pending}
            onConfirm={onConfirm}
          />
        ) : null}
      </AlertDialogContent>
    </AlertDialog>
  );
}

function PlanContent({
  requestId,
  title,
  note,
  confirmLabel,
  pending,
  onConfirm,
}: Pick<
  DestroyPlanDialogProps,
  "requestId" | "title" | "note" | "confirmLabel" | "pending" | "onConfirm"
>) {
  const plan = useDestroyPlan(requestId);
  const data = plan.data;

  return (
    <>
      <AlertDialogHeader>
        <AlertDialogTitle>{title}</AlertDialogTitle>
        <AlertDialogDescription>
          Everything listed is deleted from DigitalOcean, including any data in
          it. This can&apos;t be undone.
        </AlertDialogDescription>
      </AlertDialogHeader>
      {note ? <div className="text-sm">{note}</div> : null}
      {plan.isPending ? (
        <div
          className="flex flex-col gap-2"
          aria-label="Loading what's destroyed"
        >
          <Skeleton className="h-4 w-2/3" />
          <Skeleton className="h-4 w-1/2" />
        </div>
      ) : plan.isError ? (
        <p role="alert" className="text-sm text-destructive">
          Couldn&apos;t work out what this destroys: {plan.error.message}
        </p>
      ) : data && !data.canDestroy ? (
        <div className="flex flex-col gap-3">
          <p role="alert" className="text-sm">
            {data.reason}
          </p>
          <PlanList title="Still running" links={data.waitFor} />
        </div>
      ) : data ? (
        <div className="flex max-h-72 flex-col gap-3 overflow-y-auto">
          <PlanList
            title={
              data.destroy.length > 1 ? "Destroyed, in this order" : "Destroyed"
            }
            links={data.destroy}
            ordered
          />
          <PlanList title="Proposals cancelled" links={data.cancel} />
        </div>
      ) : null}
      <AlertDialogFooter>
        <AlertDialogCancel disabled={pending}>Keep it</AlertDialogCancel>
        <AlertDialogAction
          variant="destructive"
          disabled={pending || !data?.canDestroy}
          onClick={onConfirm}
        >
          {confirmLabel}
        </AlertDialogAction>
      </AlertDialogFooter>
    </>
  );
}

function PlanList({
  title,
  links,
  ordered = false,
}: {
  title: string;
  links: RequestLink[];
  ordered?: boolean;
}) {
  if (links.length === 0) return null;
  const List = ordered ? "ol" : "ul";
  return (
    <div className="flex flex-col gap-1">
      <p className="text-sm font-medium">{title}</p>
      <List
        aria-label={title}
        className={cn(
          "flex flex-col gap-1 pl-5 text-sm",
          ordered ? "list-decimal" : "list-disc",
        )}
      >
        {links.map((link) => (
          <li key={link.id}>
            <span className="inline-flex flex-wrap items-center gap-2">
              {linkLabel(link)}
              <StatusBadge status={link.status} />
            </span>
          </li>
        ))}
      </List>
    </div>
  );
}
