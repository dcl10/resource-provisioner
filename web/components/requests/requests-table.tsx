"use client";

import {
  type ColumnDef,
  flexRender,
  tableFeatures,
  useTable,
} from "@tanstack/react-table";
import Link from "next/link";

import { StatusBadge } from "@/components/requests/status-badge";
import { useRequests } from "@/components/requests/use-requests";
import { Skeleton } from "@/components/ui/skeleton";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import {
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
 * The requests list: type (linking to the request), name, status and when it was created, newest first. Admins see
 * everyone's requests, with an owner column. Re-reads itself while any request has a job queued or running.
 * @param isAdmin - whether to show the owner column
 * @returns the table
 */
export function RequestsTable({ isAdmin }: { isAdmin: boolean }) {
  const requests = useRequests();
  const columns = isAdmin ? [...baseColumns, ownerColumn] : baseColumns;
  const table = useTable({
    features,
    columns,
    data: requests.data ?? [],
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
                No requests yet. Ask for something in the{" "}
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
