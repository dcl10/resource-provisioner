"use client";

import {
  type ColumnDef,
  flexRender,
  tableFeatures,
  useTable,
} from "@tanstack/react-table";
import Link from "next/link";
import { useRouter } from "next/navigation";

import { useProjects } from "@/components/projects/use-projects";
import { StatusBadge } from "@/components/requests/status-badge";
import { useRequests } from "@/components/requests/use-requests";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { Skeleton } from "@/components/ui/skeleton";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import { projectPath } from "@/lib/projects";
import {
  inProject,
  type ProvisioningRequest,
  requestName,
  requestPath,
} from "@/lib/requests";

const features = tableFeatures({});

const dateFormat = new Intl.DateTimeFormat(undefined, {
  dateStyle: "medium",
  timeStyle: "short",
});

const baseColumns: ColumnDef<typeof features, ProvisioningRequest>[] = [
  {
    accessorKey: "displayName",
    header: "Type",
    cell: ({ row }) => (
      <Link
        href={requestPath(row.original.id)}
        className="font-medium underline-offset-4 hover:underline"
      >
        {row.original.displayName}
      </Link>
    ),
  },
  {
    id: "name",
    header: "Name",
    cell: ({ row }) => (
      <span className="font-mono text-xs">
        {requestName(row.original) ?? "—"}
      </span>
    ),
  },
  {
    id: "project",
    header: "Project",
    cell: ({ row }) =>
      row.original.project ? (
        <Link
          href={projectPath(row.original.project.id)}
          className="font-mono text-xs underline-offset-4 hover:underline"
        >
          {row.original.project.name}
        </Link>
      ) : (
        "—"
      ),
  },
  {
    accessorKey: "status",
    header: "Status",
    cell: ({ row }) => <StatusBadge status={row.original.status} />,
  },
  {
    accessorKey: "createdAt",
    header: "Created",
    cell: ({ row }) => dateFormat.format(new Date(row.original.createdAt)),
  },
];

const ownerColumn: ColumnDef<typeof features, ProvisioningRequest> = {
  id: "owner",
  header: "Owner",
  cell: ({ row }) => row.original.ownerEmail ?? row.original.ownerId,
};

/**
 * The requests list: type (linking to the request), name, project (linking to it), status and when it was created,
 * newest first, with a filter by project above it. Admins see everyone's requests, with an owner column. Re-reads
 * itself while any request has a job queued or running.
 * @param isAdmin - whether to show the owner column
 * @param projectId - show only this project's requests (and the `project` request that backs it); null for all
 * @returns the table
 */
export function RequestsTable({
  isAdmin,
  projectId = null,
}: {
  isAdmin: boolean;
  projectId?: string | null;
}) {
  const requests = useRequests();
  const columns = isAdmin ? [...baseColumns, ownerColumn] : baseColumns;
  const table = useTable({
    features,
    columns,
    data: inProject(requests.data ?? [], projectId),
    getRowId: (row) => row.id,
  });

  if (requests.isPending) return <RequestsTableSkeleton />;
  if (requests.isError) {
    return (
      <p role="alert" className="text-sm text-muted-foreground">
        Couldn&apos;t load your requests: {requests.error.message}
      </p>
    );
  }

  return (
    <div className="flex flex-col gap-3">
      <ProjectFilter projectId={projectId} />
      <div className="overflow-x-auto rounded-lg border">
        <Table>
          <TableHeader>
            {table.getHeaderGroups().map((headerGroup) => (
              <TableRow key={headerGroup.id}>
                {headerGroup.headers.map((header) => (
                  <TableHead key={header.id}>
                    {header.isPlaceholder
                      ? null
                      : flexRender(
                          header.column.columnDef.header,
                          header.getContext(),
                        )}
                  </TableHead>
                ))}
              </TableRow>
            ))}
          </TableHeader>
          <TableBody>
            {table.getRowModel().rows.length ? (
              table.getRowModel().rows.map((row) => (
                <TableRow key={row.id}>
                  {row.getAllCells().map((cell) => (
                    <TableCell key={cell.id}>
                      {flexRender(
                        cell.column.columnDef.cell,
                        cell.getContext(),
                      )}
                    </TableCell>
                  ))}
                </TableRow>
              ))
            ) : (
              <TableRow>
                <TableCell
                  colSpan={columns.length}
                  className="h-24 text-center text-muted-foreground"
                >
                  {projectId
                    ? "No requests in this project."
                    : "No requests yet."}{" "}
                  Ask for something in the{" "}
                  <Link href="/chat" className="underline underline-offset-4">
                    chat
                  </Link>
                  .
                </TableCell>
              </TableRow>
            )}
          </TableBody>
        </Table>
      </div>
    </div>
  );
}

/** The filter's value for "every project". */
const ALL_PROJECTS = "all";

/**
 * Filters the requests list by project, through the URL (`/requests?project=<id>`), so a filtered list can be linked
 * to and survives a reload.
 * @param projectId - the project shown, or null for all
 * @returns the select
 */
function ProjectFilter({ projectId }: { projectId: string | null }) {
  const router = useRouter();
  const projects = useProjects();
  const items = [
    { value: ALL_PROJECTS, label: "All projects" },
    ...(projects.data ?? []).map((p) => ({ value: p.id, label: p.name })),
  ];

  return (
    <Select
      items={items}
      value={projectId ?? ALL_PROJECTS}
      onValueChange={(value) =>
        router.replace(
          value && value !== ALL_PROJECTS
            ? `/requests?project=${encodeURIComponent(value)}`
            : "/requests",
        )
      }
    >
      <SelectTrigger aria-label="Project" className="min-w-48">
        <SelectValue />
      </SelectTrigger>
      <SelectContent>
        {items.map((item) => (
          <SelectItem key={item.value} value={item.value}>
            {item.label}
          </SelectItem>
        ))}
      </SelectContent>
    </Select>
  );
}

/**
 * A placeholder for the requests table.
 * @returns the skeleton
 */
export function RequestsTableSkeleton() {
  return (
    <div className="flex flex-col gap-2">
      <Skeleton className="h-9 w-full" />
      <Skeleton className="h-9 w-full" />
      <Skeleton className="h-9 w-full" />
    </div>
  );
}
