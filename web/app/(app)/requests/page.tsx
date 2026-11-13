import { Suspense } from "react";

import { RequestsList } from "@/components/requests/requests-list";
import { RequestsTableSkeleton } from "@/components/requests/requests-table";

export default function RequestsPage({ searchParams }: PageProps<"/requests">) {
  return (
    <div className="flex flex-col gap-6">
      <h1 className="text-2xl font-semibold tracking-tight">Requests</h1>
      <Suspense fallback={<RequestsTableSkeleton />}>
        <RequestsList searchParams={searchParams} />
      </Suspense>
    </div>
  );
}
