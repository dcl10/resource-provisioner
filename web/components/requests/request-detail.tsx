"use client";

import { useQueryClient } from "@tanstack/react-query";
import { ArrowLeft } from "lucide-react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { useCallback, useEffect } from "react";
import { toast } from "sonner";

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
import { isNotFound } from "@/lib/api";
import type { SessionUser } from "@/lib/auth/types";
import { PROJECT_TYPE, projectPath } from "@/lib/projects";
import {
  canDestroy,
  formatValue,
  isInProgress,
  linkLabel,
  type ProvisioningRequest,
  type RequestLink,
  requestName,
  requestPath,
  waitingFor,
} from "@/lib/requests";

const dateFormat = new Intl.DateTimeFormat(undefined, {
  dateStyle: "medium",
  timeStyle: "short",
});

/**
 * Where the page goes once the request it shows has been deleted: its project's page, or the projects list for a
 * `project` request (whose project goes with it), or the requests list for one that had no project.
 */
function afterDeleted(request: ProvisioningRequest): {
  path: string;
  label: string;
} {
  if (request.type === PROJECT_TYPE)
    return { path: "/projects", label: "your projects" };
  if (request.project)
    return { path: projectPath(request.project.id), label: "its project" };
  return { path: "/requests", label: "your requests" };
}

/**
 * One request: its status, settings, outputs (secret ones behind a reveal, for the owner), the live Terraform log,
 * and Destroy for the owner or an admin once it's provisioned or has failed (Retry destroy after a failed destroy),
 * with a link to its project. If its destroy stopped because something that depends on it failed to, it says which. Once a
 * destroy finishes the API deletes the request, so the page then answers 404 and sends the user to its project (or,
 * for a project's own request or one without a project, to the projects or requests list).
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
  const router = useRouter();
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

  // It was here and now isn't: destroyed (or, from another tab, cancelled), so the API deleted it. A failed re-read
  // keeps the last data, which is how this tells it from a request that never existed.
  const deleted = request.data !== undefined && isNotFound(request.error);
  const deletedMessage = request.data ? goneMessage(request.data) : "";
  const destination = request.data ? afterDeleted(request.data) : null;
  const destinationPath = destination?.path;
  useEffect(() => {
    if (!deleted || !destinationPath) return;
    toast(deletedMessage, { id: `deleted-${requestId}` });
    router.replace(destinationPath);
  }, [deleted, deletedMessage, destinationPath, requestId, router]);

  if (request.isPending) return <RequestDetailSkeleton />;
  if (deleted) {
    return (
      <p role="status" className="text-sm text-muted-foreground">
        {deletedMessage}. Taking you back to {destination?.label}…
      </p>
    );
  }
  if (request.isError) {
    return (
      <p role="alert" className="text-sm text-muted-foreground">
        {isNotFound(request.error)
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
  const waiting = waitingFor(data);

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
        {data.project ? (
          <p className="text-sm">
            {data.type === PROJECT_TYPE ? "Backs project " : "Project: "}
            <Link
              href={projectPath(data.project.id)}
              className="font-mono underline underline-offset-4"
            >
              {data.project.name}
            </Link>
          </p>
        ) : null}
        {waiting.length > 0 ? (
          <p className="text-sm" aria-live="polite">
            Waiting for <Links links={waiting} /> to be provisioned first.
          </p>
        ) : null}
        {data.status === "DestroyFailed" ? (
          <p className="text-sm" aria-live="polite">
            Its destroy failed, so what it created may still exist. The log
            below says why. A retry destroys only this and what depends on it.
          </p>
        ) : null}
        {data.destroyStoppedBy ? (
          <p className="text-sm" aria-live="polite">
            Destroy stopped because <Links links={[data.destroyStoppedBy]} />{" "}
            failed to destroy. Destroy this again to retry, or retry that one
            alone from its page.
          </p>
        ) : null}
      </div>

      {data.dependsOn.length > 0 || data.dependents.length > 0 ? (
        <Card>
          <CardHeader>
            <CardTitle>Related requests</CardTitle>
            <CardDescription>
              What this needs is provisioned before it. Destroying it destroys
              what needs it first.
            </CardDescription>
          </CardHeader>
          <CardContent className="flex flex-col gap-3">
            <RelatedList title="Depends on" links={data.dependsOn} />
            <RelatedList title="Needed by" links={data.dependents} />
          </CardContent>
        </Card>
      ) : null}

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
            onDeleted={refresh}
          />
        </CardContent>
      </Card>
    </div>
  );
}

function goneMessage(request: ProvisioningRequest): string {
  const name = requestName(request);
  const label = name ? `${request.displayName} ${name}` : request.displayName;
  return request.status === "Destroying"
    ? `${label} was destroyed`
    : `${label} no longer exists`;
}

function Links({ links }: { links: RequestLink[] }) {
  return links.map((link, i) => (
    <span key={link.id}>
      {i > 0 ? ", " : null}
      <Link
        href={requestPath(link.id)}
        className="underline underline-offset-4"
      >
        {linkLabel(link)}
      </Link>
    </span>
  ));
}

function RelatedList({
  title,
  links,
}: {
  title: string;
  links: RequestLink[];
}) {
  if (links.length === 0) return null;
  return (
    <div className="flex flex-col gap-1">
      <p className="text-sm font-medium">{title}</p>
      <ul className="flex flex-col gap-1">
        {links.map((link) => (
          <li key={link.id} className="flex items-center gap-2 text-sm">
            <Link
              href={requestPath(link.id)}
              className="underline underline-offset-4"
            >
              {linkLabel(link)}
            </Link>
            <StatusBadge status={link.status} />
          </li>
        ))}
      </ul>
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
