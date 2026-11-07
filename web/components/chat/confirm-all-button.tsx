"use client";

import { useMutation, useQueries, useQueryClient } from "@tanstack/react-query";
import { CheckCheck } from "lucide-react";
import { useRouter } from "next/navigation";
import { useState } from "react";
import { toast } from "sonner";

import {
  requestQueryKey,
  requestsQueryKey,
} from "@/components/requests/use-requests";
import { Button } from "@/components/ui/button";
import { ApiError, retryUnlessClientError } from "@/lib/api";
import type { Proposal } from "@/lib/chat";
import { confirmRequests, getRequest } from "@/lib/requests";

/** The problem title the API gives when confirming would exceed an active-request limit. */
const LIMIT_TITLE = "Active request limit reached";

/**
 * Confirms every proposal in a reply that still awaits confirmation, in one go: the API checks the spend limits for
 * all of them and provisions them in dependency order, or confirms none. Shown only when at least two are waiting.
 * Shares the request queries with the proposal cards, so their statuses update together.
 * @param proposals - the reply's proposals
 * @returns the button, or nothing
 */
export function ConfirmAllButton({ proposals }: { proposals: Proposal[] }) {
  const router = useRouter();
  const queryClient = useQueryClient();
  const [problem, setProblem] = useState<string | null>(null);
  const statuses = useQueries({
    queries: proposals.map((p) => ({
      queryKey: requestQueryKey(p.requestId),
      queryFn: () => getRequest(p.requestId),
      retry: retryUnlessClientError,
    })),
  });

  const pending = proposals.filter(
    (_, i) => statuses[i]?.data?.status === "AwaitingConfirmation",
  );

  const confirmAll = useMutation({
    mutationFn: () => confirmRequests(pending.map((p) => p.requestId)),
    onSuccess: (confirmed) => {
      setProblem(null);
      for (const request of confirmed) {
        queryClient.setQueryData(requestQueryKey(request.id), request);
      }
      void queryClient.invalidateQueries({
        queryKey: requestsQueryKey,
        exact: true,
      });
      toast.success(`${confirmed.length} requests confirmed`, {
        description:
          "They're provisioned in order. Follow them on the Requests page.",
        action: {
          label: "View requests",
          onClick: () => router.push("/requests"),
        },
      });
    },
    onError: (error) => {
      const message =
        error instanceof ApiError
          ? error.message
          : "Couldn't reach the app. Check your connection and try again.";
      const title =
        error instanceof ApiError && error.title === LIMIT_TITLE
          ? "You're at your limit"
          : "Nothing was confirmed";
      setProblem(message);
      toast.error(title, { description: message });
    },
  });

  if (pending.length < 2) return null;

  return (
    <div className="flex flex-col items-end gap-2">
      <Button
        disabled={confirmAll.isPending}
        onClick={() => confirmAll.mutate()}
      >
        <CheckCheck />
        Confirm all ({pending.length})
      </Button>
      {problem ? (
        <p role="alert" className="text-sm text-muted-foreground">
          {problem}
        </p>
      ) : null}
    </div>
  );
}
