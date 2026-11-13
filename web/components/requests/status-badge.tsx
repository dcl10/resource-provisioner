import { LoaderCircle } from "lucide-react";

import { Badge } from "@/components/ui/badge";
import { isInProgress, type RequestStatus, statusLabels } from "@/lib/requests";

const variants: Record<
  RequestStatus,
  "default" | "secondary" | "outline" | "destructive"
> = {
  AwaitingConfirmation: "secondary",
  Queued: "secondary",
  Applying: "secondary",
  Succeeded: "default",
  Failed: "destructive",
  Destroying: "secondary",
};

/**
 * A request's status as a badge: a short label, coloured by outcome, with a spinner while a job is queued or running.
 * @param status - the status
 * @returns the badge
 */
export function StatusBadge({ status }: { status: RequestStatus }) {
  return (
    <Badge variant={variants[status]} data-status={status}>
      {isInProgress(status) ? (
        <LoaderCircle className="animate-spin" aria-hidden />
      ) : null}
      {statusLabels[status]}
    </Badge>
  );
}
