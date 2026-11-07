"use client";

import { useQuery } from "@tanstack/react-query";

import { getConversation, listConversations } from "@/lib/chat";
import { getRequest } from "@/lib/requests";

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
 * TanStack Query key for one request.
 * @param id - the request ID
 * @returns the key
 */
export function requestQueryKey(id: string) {
  return ["requests", id] as const;
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
  });
}

/**
 * One request, for a proposal card's current status.
 * @param id - the request ID
 * @returns the query
 */
export function useRequest(id: string) {
  return useQuery({
    queryKey: requestQueryKey(id),
    queryFn: () => getRequest(id),
  });
}
