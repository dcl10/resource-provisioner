"use client";

import { ArrowLeft } from "lucide-react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { useEffect } from "react";
import { toast } from "sonner";

import { NewConversationButton } from "@/components/chat/conversation-list";
import { useProject } from "@/components/projects/use-projects";
import { StatusBadge } from "@/components/requests/status-badge";
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
import { type ResourceNode, resourceTree } from "@/lib/projects";
import { linkLabel, requestName, requestPath } from "@/lib/requests";

const dateFormat = new Intl.DateTimeFormat(undefined, {
  dateStyle: "medium",
  timeStyle: "short",
});

/**
 * One project: its status and settings, its resources (each nested under what it depends on, linking to its request
 * page), and a button to start a chat to add more. Re-reads itself while anything in it has a job queued or running.
 * Once the project is gone (its `project` request destroyed or cancelled), it says so and goes to the projects list.
 * @param projectId - the project
 * @param user - the signed-in user, to tell their own project from someone else's
 * @returns the page content
 */
export function ProjectDetail({
  projectId,
  user,
}: {
  projectId: string;
  user: Pick<SessionUser, "id" | "isAdmin">;
}) {
  const router = useRouter();
  const project = useProject(projectId);

  // It was here and now isn't, so the API deleted it. A failed re-read keeps the last data, which is how this tells
  // it from a project that never existed.
  const deleted = project.data !== undefined && isNotFound(project.error);
  const deletedMessage = project.data
    ? `Project ${project.data.name} no longer exists`
    : "";
  useEffect(() => {
    if (!deleted) return;
    toast(deletedMessage, { id: `deleted-project-${projectId}` });
    router.replace("/projects");
  }, [deleted, deletedMessage, projectId, router]);

  if (project.isPending) return <ProjectDetailSkeleton />;
  if (deleted) {
    return (
      <p role="status" className="text-sm text-muted-foreground">
        {deletedMessage}. Taking you back to your projects…
      </p>
    );
  }
  if (project.isError) {
    return (
      <p role="alert" className="text-sm text-muted-foreground">
        {isNotFound(project.error)
          ? "This project doesn't exist, or isn't yours."
          : `Couldn't load this project: ${project.error.message}`}
      </p>
    );
  }

  const data = project.data;
  const isOwner = data.ownerId === user.id;
  const tree = resourceTree(data.resources);

  return (
    <div className="flex flex-col gap-6">
      <div className="flex flex-col gap-3">
        <Link
          href="/projects"
          className="flex w-fit items-center gap-1 text-sm text-muted-foreground hover:text-foreground"
        >
          <ArrowLeft className="size-4" />
          All projects
        </Link>
        <div className="flex flex-wrap items-center gap-3">
          <h1 className="font-mono text-2xl font-semibold tracking-tight">
            {data.name}
          </h1>
          <StatusBadge status={data.status} />
          {isOwner ? (
            <div className="ml-auto flex flex-wrap items-center gap-2">
              <NewConversationButton variant="outline" label="Start a chat" />
            </div>
          ) : null}
        </div>
        <p className="text-sm text-muted-foreground">
          Created {dateFormat.format(new Date(data.createdAt))}
          {isOwner ? "" : ` · Owner: ${data.ownerEmail ?? data.ownerId}`}
        </p>
      </div>

      <Card>
        <CardHeader>
          <CardTitle>Resources</CardTitle>
          <CardDescription>
            What&apos;s in the project. Anything that depends on another
            resource is shown under it.
          </CardDescription>
        </CardHeader>
        <CardContent>
          {tree.length > 0 ? (
            <ResourceList nodes={tree} label="Resources" />
          ) : (
            <p className="text-sm text-muted-foreground">
              Nothing in this project yet.
              {isOwner ? " Start a chat and ask for what you need in it." : ""}
            </p>
          )}
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle>Settings</CardTitle>
          <CardDescription>
            The project&apos;s own log and outputs are on{" "}
            <Link
              href={requestPath(data.projectRequestId)}
              className="underline underline-offset-4"
            >
              its request
            </Link>
            .
          </CardDescription>
        </CardHeader>
        <CardContent>
          <Table>
            <TableBody>
              <SettingRow label="Description" value={data.description} />
              <SettingRow label="Purpose" value={data.purpose} />
              <SettingRow label="Environment" value={data.environment} />
            </TableBody>
          </Table>
        </CardContent>
      </Card>
    </div>
  );
}

function SettingRow({ label, value }: { label: string; value: string | null }) {
  return (
    <TableRow>
      <TableCell className="w-1/3 text-muted-foreground">{label}</TableCell>
      <TableCell className="whitespace-pre-wrap">{value || "—"}</TableCell>
    </TableRow>
  );
}

function ResourceList({
  nodes,
  label,
}: {
  nodes: ResourceNode[];
  label: string;
}) {
  return (
    <ul aria-label={label} className="flex flex-col gap-2">
      {nodes.map(({ request, children, alsoAfter }) => {
        const name = requestName(request);
        return (
          <li key={request.id} className="flex flex-col gap-2">
            <div className="flex flex-wrap items-center gap-2 text-sm">
              <Link
                href={requestPath(request.id)}
                className="font-medium underline-offset-4 hover:underline"
              >
                {request.displayName}
              </Link>
              {name ? <span className="font-mono text-xs">{name}</span> : null}
              <StatusBadge status={request.status} />
              {alsoAfter.length > 0 ? (
                <span className="text-xs text-muted-foreground">
                  also needs {alsoAfter.map(linkLabel).join(", ")}
                </span>
              ) : null}
            </div>
            {children.length > 0 ? (
              <div className="ml-2 border-l pl-4">
                <ResourceList
                  nodes={children}
                  label={`Depends on ${linkLabel({ ...request, name })}`}
                />
              </div>
            ) : null}
          </li>
        );
      })}
    </ul>
  );
}

/**
 * A placeholder for the project page.
 * @returns the skeleton
 */
export function ProjectDetailSkeleton() {
  return (
    <div className="flex flex-col gap-6">
      <Skeleton className="h-8 w-1/2" />
      <Skeleton className="h-40 w-full" />
      <Skeleton className="h-40 w-full" />
    </div>
  );
}
