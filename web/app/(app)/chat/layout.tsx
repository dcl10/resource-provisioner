import { Suspense } from "react";

import {
  ConversationList,
  ConversationListSkeleton,
} from "@/components/chat/conversation-list";

export default function ChatLayout({ children }: LayoutProps<"/chat">) {
  return (
    <div className="flex h-[calc(100dvh-7rem)] min-h-96 flex-col gap-6 md:flex-row">
      <aside className="max-h-48 shrink-0 overflow-y-auto md:max-h-none md:w-64">
        {/* The list reads the route's id, which is only known per request. */}
        <Suspense fallback={<ConversationListSkeleton />}>
          <ConversationList />
        </Suspense>
      </aside>
      <section className="flex min-h-0 min-w-0 flex-1 flex-col">
        {children}
      </section>
    </div>
  );
}
