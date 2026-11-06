"use client";

import { InvitationsTable } from "@/components/admin/invitations-table";
import { InviteForm } from "@/components/admin/invite-form";
import { useInvitations } from "@/components/admin/use-invitations";
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";
import { Skeleton } from "@/components/ui/skeleton";

/**
 * The admin Users page body: the invite form, then the invitations and their status. Only rendered for admins; the
 * API refuses everyone else anyway.
 * @returns the page content
 */
export function UsersAdmin() {
  const invitations = useInvitations();

  return (
    <div className="flex flex-col gap-6">
      <Card>
        <CardHeader>
          <CardTitle>Invite a colleague</CardTitle>
          <CardDescription>
            Sign-up is invite-only. Invited colleagues can chat and provision
            resources; admin rights are given in Keycloak.
          </CardDescription>
        </CardHeader>
        <CardContent>
          <InviteForm />
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle>Invitations</CardTitle>
          <CardDescription>
            Accepted once the colleague has verified their email and set a
            password.
          </CardDescription>
        </CardHeader>
        <CardContent>
          {invitations.isPending ? (
            <InvitationsSkeleton />
          ) : invitations.isError ? (
            <p role="alert" className="text-sm text-destructive">
              Couldn&apos;t load invitations: {invitations.error.message}
            </p>
          ) : (
            <InvitationsTable invitations={invitations.data} />
          )}
        </CardContent>
      </Card>
    </div>
  );
}

/**
 * Placeholder rows while the invitations load.
 * @returns the skeleton
 */
export function InvitationsSkeleton() {
  return (
    <div className="flex flex-col gap-2" aria-label="Loading invitations">
      <Skeleton className="h-10 w-full" />
      <Skeleton className="h-10 w-full" />
      <Skeleton className="h-10 w-full" />
    </div>
  );
}
