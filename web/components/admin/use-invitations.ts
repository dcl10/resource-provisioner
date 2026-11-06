"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";

import {
  inviteUser,
  listInvitations,
  resendInvitation,
  revokeInvitation,
} from "@/lib/invitations";

/** TanStack Query key for the invitations list. */
export const invitationsQueryKey = ["invitations"] as const;

/**
 * The invitations list, from `GET /api/invitations`.
 * @returns the query
 */
export function useInvitations() {
  return useQuery({ queryKey: invitationsQueryKey, queryFn: listInvitations });
}

/**
 * Mutations for inviting, resending and revoking. Each refreshes the list when it settles.
 * @returns the three mutations
 */
export function useInvitationMutations() {
  const queryClient = useQueryClient();
  const onSettled = () =>
    queryClient.invalidateQueries({ queryKey: invitationsQueryKey });

  return {
    invite: useMutation({ mutationFn: inviteUser, onSettled }),
    resend: useMutation({ mutationFn: resendInvitation, onSettled }),
    revoke: useMutation({ mutationFn: revokeInvitation, onSettled }),
  };
}
