"use client";

import { useMutation, useQueryClient } from "@tanstack/react-query";
import { MessageSquarePlus } from "lucide-react";
import Link from "next/link";
import { useParams, useRouter } from "next/navigation";
import { toast } from "sonner";

import {
  conversationsQueryKey,
  useConversations,
} from "@/components/chat/use-chat";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import { ApiError } from "@/lib/api";
import { createConversation } from "@/lib/chat";
import { cn } from "@/lib/utils";

/**
 * A button that starts a conversation and opens it.
 * @param className - extra classes
 * @returns the button
 */
export function NewConversationButton({ className }: { className?: string }) {
  const router = useRouter();
  const queryClient = useQueryClient();
  const create = useMutation({
    mutationFn: createConversation,
    onSuccess: (conversation) => {
      void queryClient.invalidateQueries({ queryKey: conversationsQueryKey });
      router.push(`/chat/${conversation.id}`);
    },
    onError: (error) =>
      toast.error("Couldn't start a conversation", {
        description: error instanceof ApiError ? error.message : undefined,
      }),
  });

  return (
    <Button
      className={className}
      disabled={create.isPending}
      onClick={() => create.mutate()}
    >
      <MessageSquarePlus />
      New conversation
    </Button>
  );
}

/**
 * The user's past conversations, most recent first, with the open one highlighted and a button to start another.
 * Reads the route's `id`, so it must sit inside a `<Suspense>` boundary.
 * @returns the list
 */
export function ConversationList() {
  const { id: activeId } = useParams<{ id?: string }>();
  const conversations = useConversations();

  return (
    <nav aria-label="Conversations" className="flex flex-col gap-3">
      <NewConversationButton className="w-full" />
      {conversations.isPending ? (
        <ConversationListSkeleton />
      ) : conversations.isError ? (
        <p className="text-sm text-muted-foreground">
          Couldn&apos;t load your conversations.
        </p>
      ) : conversations.data.length === 0 ? (
        <p className="text-sm text-muted-foreground">No conversations yet.</p>
      ) : (
        <ul className="flex flex-col gap-1">
          {conversations.data.map((conversation) => (
            <li key={conversation.id}>
              <Link
                href={`/chat/${conversation.id}`}
                aria-current={conversation.id === activeId ? "page" : undefined}
                className={cn(
                  "block truncate rounded-md px-2 py-1.5 text-sm hover:bg-muted",
                  conversation.id === activeId && "bg-muted font-medium",
                )}
              >
                {conversation.title ?? "New conversation"}
              </Link>
            </li>
          ))}
        </ul>
      )}
    </nav>
  );
}

/**
 * A placeholder for the conversation list.
 * @returns the skeleton
 */
export function ConversationListSkeleton() {
  return (
    <div className="flex flex-col gap-2">
      <Skeleton className="h-7 w-full" />
      <Skeleton className="h-7 w-full" />
      <Skeleton className="h-7 w-2/3" />
    </div>
  );
}
