"use client";

import { ChatThread } from "@/components/chat/chat-thread";
import { useConversation } from "@/components/chat/use-chat";
import { Skeleton } from "@/components/ui/skeleton";
import { ApiError } from "@/lib/api";

/**
 * Loads a conversation and shows it as a chat thread.
 * @param conversationId - the conversation
 * @returns the thread, or a placeholder while it loads
 */
export function ChatConversation({
  conversationId,
}: {
  conversationId: string;
}) {
  const conversation = useConversation(conversationId);

  if (conversation.isPending) return <ChatSkeleton />;
  if (conversation.isError) {
    const notFound =
      conversation.error instanceof ApiError &&
      conversation.error.status === 404;
    return (
      <p role="alert" className="m-auto text-sm text-muted-foreground">
        {notFound
          ? "This conversation doesn't exist, or isn't yours."
          : "Couldn't load this conversation. Try again shortly."}
      </p>
    );
  }

  return (
    <ChatThread
      conversationId={conversationId}
      initialMessages={conversation.data.messages.map((m) => ({
        id: String(m.id),
        role: m.role,
        text: m.text,
        proposals: m.proposals,
      }))}
    />
  );
}

/**
 * A placeholder for the chat while it loads.
 * @returns the skeleton
 */
export function ChatSkeleton() {
  return (
    <div className="flex flex-1 flex-col gap-4">
      <Skeleton className="ml-auto h-10 w-1/2" />
      <Skeleton className="h-24 w-3/4" />
      <Skeleton className="mt-auto h-11 w-full" />
    </div>
  );
}
