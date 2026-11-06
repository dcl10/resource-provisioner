"use client";

import {
  type ColumnDef,
  flexRender,
  tableFeatures,
  useTable,
} from "@tanstack/react-table";
import { Ban, RotateCw } from "lucide-react";
import { toast } from "sonner";

import { useInvitationMutations } from "@/components/admin/use-invitations";
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
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";
import {
  type Invitation,
  type InvitationStatus,
  isOpen,
} from "@/lib/invitations";

const features = tableFeatures({});

const dateFormat = new Intl.DateTimeFormat(undefined, {
  dateStyle: "medium",
  timeStyle: "short",
});

const statusVariants: Record<
  InvitationStatus,
  "default" | "secondary" | "outline" | "destructive"
> = {
  Pending: "secondary",
  Accepted: "default",
  Expired: "outline",
  Revoked: "destructive",
};

function InvitationActions({ invitation }: { invitation: Invitation }) {
  const { resend, revoke } = useInvitationMutations();
  if (!isOpen(invitation.status)) return null;

  const busy = resend.isPending || revoke.isPending;

  const onResend = () =>
    resend.mutate(invitation.id, {
      onSuccess: () =>
        toast.success(`Invitation resent to ${invitation.email}`),
      onError: (error) => toast.error(error.message),
    });

  const onRevoke = () =>
    revoke.mutate(invitation.id, {
      onSuccess: () =>
        toast.success(`Invitation to ${invitation.email} revoked`),
      onError: (error) => toast.error(error.message),
    });

  return (
    <div className="flex justify-end gap-2">
      <Button variant="outline" size="sm" disabled={busy} onClick={onResend}>
        <RotateCw />
        Resend
      </Button>
      <AlertDialog>
        <AlertDialogTrigger
          render={<Button variant="destructive" size="sm" disabled={busy} />}
        >
          <Ban />
          Revoke
        </AlertDialogTrigger>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Revoke this invitation?</AlertDialogTitle>
            <AlertDialogDescription>
              {invitation.email}&apos;s account will be disabled, so the link in
              their email stops working. This can&apos;t be undone from the app.
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel>Cancel</AlertDialogCancel>
            <AlertDialogAction variant="destructive" onClick={onRevoke}>
              Revoke
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </div>
  );
}

const columns: ColumnDef<typeof features, Invitation>[] = [
  { accessorKey: "email", header: "Email" },
  {
    accessorKey: "status",
    header: "Status",
    cell: ({ row }) => (
      <Badge variant={statusVariants[row.original.status]}>
        {row.original.status}
      </Badge>
    ),
  },
  {
    accessorKey: "sentAt",
    header: "Sent",
    cell: ({ row }) => dateFormat.format(new Date(row.original.sentAt)),
  },
  {
    accessorKey: "expiresAt",
    header: "Link expires",
    cell: ({ row }) =>
      row.original.status === "Accepted" || row.original.status === "Revoked"
        ? "—"
        : dateFormat.format(new Date(row.original.expiresAt)),
  },
  {
    id: "actions",
    header: () => <span className="sr-only">Actions</span>,
    cell: ({ row }) => <InvitationActions invitation={row.original} />,
  },
];

/**
 * Every invitation with its status, and resend and revoke buttons for those not yet accepted or revoked.
 * @param invitations - the invitations to show, newest first
 * @returns the table
 */
export function InvitationsTable({
  invitations,
}: {
  invitations: Invitation[];
}) {
  const table = useTable({
    features,
    columns,
    data: invitations,
    getRowId: (row) => row.id,
  });

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
                No invitations yet.
              </TableCell>
            </TableRow>
          )}
        </TableBody>
      </Table>
    </div>
  );
}
