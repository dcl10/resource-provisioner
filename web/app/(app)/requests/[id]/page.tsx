import { redirect } from "next/navigation";
import { Suspense } from "react";

import {
  RequestDetail,
  RequestDetailSkeleton,
} from "@/components/requests/request-detail";
import { getCurrentUser } from "@/lib/auth/session";

export default function RequestPage({ params }: PageProps<"/requests/[id]">) {
  return (
    <Suspense fallback={<RequestDetailSkeleton />}>
      <RequestForUser params={params} />
    </Suspense>
  );
}

/** Waits for the route's id and the session, both only known per request. */
async function RequestForUser({ params }: { params: Promise<{ id: string }> }) {
  const [{ id }, user] = await Promise.all([params, getCurrentUser()]);
  if (!user) redirect("/sign-in");

  return (
    <RequestDetail
      key={id}
      requestId={id}
      user={{ id: user.id, isAdmin: user.isAdmin }}
    />
  );
}
