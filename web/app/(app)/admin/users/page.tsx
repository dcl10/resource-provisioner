import { Suspense } from "react";

import { AdminOnly } from "@/components/admin/admin-only";
import {
  InvitationsSkeleton,
  UsersAdmin,
} from "@/components/admin/users-admin";

export default function UsersPage() {
  return (
    <div className="flex flex-col gap-6">
      <h1 className="text-2xl font-semibold tracking-tight">Users</h1>
      <Suspense fallback={<InvitationsSkeleton />}>
        <AdminOnly>
          <UsersAdmin />
        </AdminOnly>
      </Suspense>
    </div>
  );
}
