"use client";

import {
  type ColumnDef,
  flexRender,
  tableFeatures,
  useTable,
} from "@tanstack/react-table";
import Link from "next/link";

import { useProjects } from "@/components/projects/use-projects";
import { RequestsTableSkeleton } from "@/components/requests/requests-table";
import { StatusBadge } from "@/components/requests/status-badge";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import { type Project, projectPath } from "@/lib/projects";

const features = tableFeatures({});

const dateFormat = new Intl.DateTimeFormat(undefined, {
  dateStyle: "medium",
  timeStyle: "short",
});

const baseColumns: ColumnDef<typeof features, Project>[] = [
  {
    accessorKey: "name",
    header: "Name",
    cell: ({ row }) => (
      <Link
        href={projectPath(row.original.id)}
        className="font-mono text-xs font-medium underline-offset-4 hover:underline"
      >
        {row.original.name}
      </Link>
    ),
  },
  {
    accessorKey: "status",
    header: "Status",
    cell: ({ row }) => <StatusBadge status={row.original.status} />,
  },
  {
    accessorKey: "environment",
    header: "Environment",
    cell: ({ row }) => row.original.environment ?? "—",
  },
  {
    accessorKey: "resourceCount",
    header: "Resources",
    cell: ({ row }) => row.original.resourceCount,
  },
  {
    accessorKey: "createdAt",
    header: "Created",
    cell: ({ row }) => dateFormat.format(new Date(row.original.createdAt)),
  },
];

const ownerColumn: ColumnDef<typeof features, Project> = {
  id: "owner",
  header: "Owner",
  cell: ({ row }) => row.original.ownerEmail ?? row.original.ownerId,
};

/**
 * The projects list: name (linking to the project), status, environment, how many resources are in it and when it was
 * created, newest first. Admins see everyone's projects, with an owner column. Re-reads itself while any project has
 * a job queued or running.
 * @param isAdmin - whether to show the owner column
 * @returns the table
 */
export function ProjectsTable({ isAdmin }: { isAdmin: boolean }) {
  const projects = useProjects();
  const columns = isAdmin ? [...baseColumns, ownerColumn] : baseColumns;
  const table = useTable({
    features,
    columns,
    data: projects.data ?? [],
    getRowId: (row) => row.id,
  });

  if (projects.isPending) return <RequestsTableSkeleton />;
  if (projects.isError) {
    return (
      <p role="alert" className="text-sm text-muted-foreground">
        Couldn&apos;t load your projects: {projects.error.message}
      </p>
    );
  }

  return (
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
                    {flexRender(cell.column.columnDef.cell, cell.getContext())}
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
                No projects yet. Create one with New project, or ask for one in
                the{" "}
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
  );
}
