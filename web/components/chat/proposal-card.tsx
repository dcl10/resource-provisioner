"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { Check, ExternalLink, X } from "lucide-react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { useState } from "react";
import { toast } from "sonner";

import { requestQueryKey, useRequest } from "@/components/chat/use-chat";
import { Badge } from "@/components/ui/badge";
import { Button, buttonVariants } from "@/components/ui/button";
import {
  Card,
  CardAction,
  CardContent,
  CardDescription,
  CardFooter,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";
import { Table, TableBody, TableCell, TableRow } from "@/components/ui/table";
import { ApiError } from "@/lib/api";
import type { Proposal } from "@/lib/chat";
import {
  cancelRequest,
  confirmRequest,
  type ProvisioningRequest,
  requestPath,
  statusLabels,
} from "@/lib/requests";

/** The problem title the API gives when confirming would exceed an active-request limit. */
const LIMIT_TITLE = "Active request limit reached";

/** A problem shown on the card, with an optional heading. */
interface CardProblem {
  title?: string;
  message: string;
}

function toProblem(error: unknown): CardProblem {
  if (error instanceof ApiError) {
    return error.title === LIMIT_TITLE
      ? { title: "You're at your limit", message: error.message }
      : { message: error.message };
  }
  return {
    message: "Couldn't reach the app. Check your connection and try again.",
  };
}

/**
 * The confirmation card for a request the agent proposed: the resource type, its variables, the cost hint, and
 * Confirm and Cancel buttons while it awaits confirmation. Confirming is the only thing that starts provisioning.
 * The card reads the request's current status, so a proposal confirmed or cancelled earlier shows that instead.
 * @param proposal - the proposed request
 * @returns the card
 */
export function ProposalCard({ proposal }: { proposal: Proposal }) {
  const { requestId } = proposal;
  const router = useRouter();
  const queryClient = useQueryClient();
  const request = useRequest(requestId);
  const [problem, setProblem] = useState<CardProblem | null>(null);

  const onSuccess = (updated: ProvisioningRequest) => {
    setProblem(null);
    queryClient.setQueryData(requestQueryKey(requestId), updated);
    void queryClient.invalidateQueries({ queryKey: ["requests"] });
  };
  const onError = (error: unknown) => {
    const next = toProblem(error);
    setProblem(next);
    toast.error(next.title ?? "That didn't work", {
      description: next.message,
    });
  };

  const confirm = useMutation({
    mutationFn: () => confirmRequest(requestId),
    onSuccess: (updated) => {
      onSuccess(updated);
      toast.success(`${proposal.displayName} confirmed`, {
        description:
          "Provisioning has started. Follow it on the request's page.",
        action: {
          label: "View request",
          onClick: () => router.push(requestPath(requestId)),
        },
      });
    },
    onError,
  });
  const cancel = useMutation({
    mutationFn: () => cancelRequest(requestId),
    onSuccess: (updated) => {
      onSuccess(updated);
      toast(`${proposal.displayName} proposal cancelled`);
    },
    onError,
  });

  const status = request.data?.status;
  const awaiting = status === "AwaitingConfirmation";
  // While the status loads, the same two buttons show disabled, so they don't change under the pointer.
  const showActions = awaiting || request.isPending;
  const disabled = !awaiting || confirm.isPending || cancel.isPending;

  return (
    <Card size="sm" aria-label={`Proposed ${proposal.displayName}`}>
      <CardHeader>
        <CardTitle>{proposal.displayName}</CardTitle>
        <CardDescription>
          Proposed request · <span className="font-mono">{proposal.type}</span>
        </CardDescription>
        <CardAction>
          {status ? (
            <Badge variant={awaiting ? "secondary" : "outline"}>
              {statusLabels[status]}
            </Badge>
          ) : null}
        </CardAction>
      </CardHeader>
      <CardContent className="flex flex-col gap-3">
        <Table>
          <TableBody>
            {proposal.items.map((item) => (
              <TableRow key={item.name}>
                <TableCell className="w-1/3 text-muted-foreground">
                  {item.label}
                </TableCell>
                <TableCell className="font-mono text-xs whitespace-pre-wrap">
                  {item.value}
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
        <p className="text-sm">
          <span className="font-medium">Cost: </span>
          <span className="text-muted-foreground">{proposal.costHint}</span>
        </p>
        {problem ? (
          <div
            role="alert"
            className="rounded-md border border-destructive/40 bg-destructive/5 p-3 text-sm"
          >
            {problem.title ? (
              <p className="font-medium">{problem.title}</p>
            ) : null}
            <p className="text-muted-foreground">{problem.message}</p>
          </div>
        ) : null}
        {request.isError ? (
          <p className="text-sm text-muted-foreground">
            Couldn&apos;t load this request&apos;s status.
          </p>
        ) : null}
      </CardContent>
      <CardFooter className="justify-end gap-2">
        {showActions ? (
          <>
            <Button
              variant="outline"
              disabled={disabled}
              onClick={() => cancel.mutate()}
            >
              <X />
              Cancel
            </Button>
            <Button disabled={disabled} onClick={() => confirm.mutate()}>
              <Check />
              Confirm
            </Button>
          </>
        ) : (
          <Link
            href={requestPath(requestId)}
            className={buttonVariants({ variant: "outline" })}
          >
            <ExternalLink />
            View request
          </Link>
        )}
      </CardFooter>
    </Card>
  );
}
