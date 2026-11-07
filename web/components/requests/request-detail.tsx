"use client";

import { useQueryClient } from "@tanstack/react-query";
import { ArrowLeft } from "lucide-react";
import Link from "next/link";
import { useCallback } from "react";

import { DestroyButton } from "@/components/requests/destroy-button";
import { RequestLog } from "@/components/requests/request-log";
import { SecretOutputs } from "@/components/requests/secret-outputs";
import { StatusBadge } from "@/components/requests/status-badge";
import {
  requestQueryKey,
  requestsQueryKey,
  useCatalog,
  useRequest,
} from "@/components/requests/use-requests";
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";
import { Skeleton } from "@/components/ui/skeleton";
import { Table, TableBody, TableCell, TableRow } from "@/components/ui/table";
import { ApiError } from "@/lib/api";
import type { SessionUser } from "@/lib/auth/types";
import {
  canDestroy,
  formatValue,
  isInProgress,
  requestName,
} from "@/lib/requests";

const dateFormat = new Intl.DateTimeFormat(undefined, {
  dateStyle: "medium",
  timeStyle: "short",
});

/**
 * One request: its status, settings, outputs (secret ones behind a reveal, for the owner), the live Terraform log,
 * and Destroy for the owner or an admin once it's provisioned or has failed.
 * @param requestId - the request
 * @param user - the signed-in user, to decide who may reveal secrets and destroy
 * @returns the page content
 */
export function RequestDetail({
  requestId,
  user,
}: {
  requestId: string;
  user: Pick<SessionUser, "id" | "isAdmin">;
}) {
  const queryClient = useQueryClient();
  const request = useRequest(requestId);
  const catalog = useCatalog();
  const refresh = useCallback(() => {
    void queryClient.invalidateQueries({
      queryKey: requestQueryKey(requestId),
    });
    void queryClient.invalidateQueries({
      queryKey: requestsQueryKey,
      exact: true,
    });
  }, [queryClient, requestId]);

  if (request.isPending) return <RequestDetailSkeleton />;
  if (request.isError) {
    const notFound =
      request.error instanceof ApiError && request.error.status === 404;
    return (
      <p role="alert" className="text-sm text-muted-foreground">
        {notFound
          ? "This request doesn't exist, or isn't yours."
          : `Couldn't load this request: ${request.error.message}`}
      </p>
    );
  }

  const data = request.data;
  const name = requestName(data);
  const isOwner = data.ownerId === user.id;
  const outputSpecs =
    catalog.data?.find((type) => type.id === data.type)?.outputs ?? [];
  const secretSpecs = outputSpecs.filter((o) => o.sensitive);
  const outputs = Object.entries(data.outputs ?? {});

  return (
    <div className="flex flex-col gap-6">
      <div className="flex flex-col gap-3">
        <Link
          href="/requests"
          className="flex w-fit items-center gap-1 text-sm text-muted-foreground hover:text-foreground"
        >
          <ArrowLeft className="size-4" />
          All requests
        </Link>
        <div className="flex flex-wrap items-center gap-3">
          <h1 className="text-2xl font-semibold tracking-tight">
            {data.displayName}
            {name ? (
              <span className="font-mono text-muted-foreground"> {name}</span>
            ) : null}
          </h1>
          <StatusBadge status={data.status} />
          {canDestroy(data.status) && (isOwner || user.isAdmin) ? (
            <div className="ml-auto">
              <DestroyButton request={data} />
            </div>
          ) : null}
        </div>
        <p className="text-sm text-muted-foreground">
          Created {dateFormat.format(new Date(data.createdAt))}
          {data.confirmedAt
            ? `, confirmed ${dateFormat.format(new Date(data.confirmedAt))}`
            : ""}
          {isOwner ? "" : ` · Owner: ${data.ownerEmail ?? data.ownerId}`}
        </p>
      </div>

      <Card>
        <CardHeader>
          <CardTitle>Settings</CardTitle>
          <CardDescription>
            What was asked for, with defaults applied.
          </CardDescription>
        </CardHeader>
        <CardContent>
          <ValuesTable values={Object.entries(data.variables)} />
        </CardContent>
      </Card>

      {data.status === "Succeeded" || outputs.length > 0 ? (
        <Card>
          <CardHeader>
            <CardTitle>Outputs</CardTitle>
            <CardDescription>
              How to reach and use what was created.
            </CardDescription>
          </CardHeader>
          <CardContent className="flex flex-col gap-4">
            {outputs.length > 0 ? (
              <ValuesTable
                values={outputs}
                descriptions={Object.fromEntries(
                  outputSpecs.map((o) => [o.name, o.description]),
                )}
              />
            ) : null}
            {isOwner &&
            data.status === "Succeeded" &&
            secretSpecs.length > 0 ? (
              <SecretOutputs requestId={data.id} outputs={secretSpecs} />
            ) : null}
          </CardContent>
        </Card>
      ) : null}

      <Card>
        <CardHeader>
          <CardTitle>Log</CardTitle>
          <CardDescription>
            Terraform&apos;s output, live while a job runs.
          </CardDescription>
        </CardHeader>
        <CardContent>
          <RequestLog
            requestId={data.id}
            follow={isInProgress(data.status)}
            onStatus={refresh}
          />
        </CardContent>
      </Card>
    </div>
  );
}

function ValuesTable({
  values,
  descriptions = {},
}: {
  values: [string, unknown][];
  descriptions?: Record<string, string>;
}) {
  return (
    <Table>
      <TableBody>
        {values.map(([key, value]) => (
          <TableRow key={key}>
            <TableCell className="w-1/3 align-top">
              <div className="font-mono text-xs">{key}</div>
              {descriptions[key] ? (
                <div className="text-xs text-muted-foreground">
                  {descriptions[key]}
                </div>
              ) : null}
            </TableCell>
            <TableCell className="font-mono text-xs break-all whitespace-pre-wrap">
              {formatValue(value)}
            </TableCell>
          </TableRow>
        ))}
      </TableBody>
    </Table>
  );
}

/**
 * A placeholder for the request page.
 * @returns the skeleton
 */
export function RequestDetailSkeleton() {
  return (
    <div className="flex flex-col gap-6">
      <Skeleton className="h-8 w-1/2" />
      <Skeleton className="h-40 w-full" />
      <Skeleton className="h-64 w-full" />
    </div>
  );
}
