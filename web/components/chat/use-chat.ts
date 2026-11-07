"use client";

import { useQuery } from "@tanstack/react-query";

import { retryUnlessClientError } from "@/lib/api";
import { getConversation, listConversations } from "@/lib/chat";

/** TanStack Query key for the conversation list. */
export const conversationsQueryKey = ["conversations"] as const;

/**
 * TanStack Query key for one conversation.
 * @param id - the conversation ID
 * @returns the key
 */
export function conversationQueryKey(id: string) {
  return ["conversations", id] as const;
}

/**
 * The user's conversations, from `GET /api/conversations`.
 * @returns the query
 */
export function useConversations() {
  return useQuery({
    queryKey: conversationsQueryKey,
    queryFn: listConversations,
  });
}

/**
 * One conversation with its messages, from `GET /api/conversations/{id}`.
 * @param id - the conversation ID
 * @returns the query
 */
export function useConversation(id: string) {
  return useQuery({
    queryKey: conversationQueryKey(id),
    queryFn: () => getConversation(id),
    retry: retryUnlessClientError,
  });
}
