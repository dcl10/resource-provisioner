import { z } from "zod";

import { ApiError, readApiError } from "@/lib/api";

/** Where an invitation has got to, as the API derives it. */
export type InvitationStatus = "Pending" | "Accepted" | "Expired" | "Revoked";

/** An invitation as `GET /api/invitations` returns it. Dates are ISO 8601 strings. */
export interface Invitation {
  id: string;
  email: string;
  status: InvitationStatus;
  invitedBy: string;
  sentAt: string;
  expiresAt: string;
  revokedAt: string | null;
}

/** The invite form's fields and their validation. The API checks the same rules again. */
export const inviteSchema = z.object({
  email: z
    .string()
    .trim()
    .min(1, "Enter an email address.")
    .max(254, "That email address is too long.")
    .pipe(z.email("Enter a valid email address.")),
});

/** Values the invite form submits. */
export type InviteValues = z.infer<typeof inviteSchema>;

/** Base path of the invitations endpoints, through the API proxy. */
export const INVITATIONS_URL = "/api/proxy/invitations";

export { ApiError } from "@/lib/api";

/**
 * Whether an invitation can be sent again or revoked: only while it hasn't been accepted or revoked.
 * @param status - the invitation's status
 * @returns whether resend and revoke apply
 */
export function isOpen(status: InvitationStatus): boolean {
  return status === "Pending" || status === "Expired";
}

/**
 * Reads the message from an API error response. The API sends problem details: `detail` for 404, 409 and 502, and
 * `errors` (field to messages) for validation failures.
 * @param response - the failed response
 * @returns a message to show the admin
 */
export async function readErrorMessage(response: Response): Promise<string> {
  if (response.status === 403) {
    return "Only admins can manage invitations.";
  }
  return (await readApiError(response)).message;
}

async function send<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(`${INVITATIONS_URL}${path}`, {
    ...init,
    headers: { Accept: "application/json", ...init?.headers },
  });
  if (!response.ok) {
    throw new ApiError(await readErrorMessage(response), response.status);
  }
  return (await response.json()) as T;
}

/**
 * Lists every invitation, newest first.
 * @returns the invitations
 */
export function listInvitations(): Promise<Invitation[]> {
  return send<Invitation[]>("");
}

/**
 * Invites a colleague by email.
 * @param values - the invite form's values
 * @returns the new invitation
 */
export function inviteUser(values: InviteValues): Promise<Invitation> {
  return send<Invitation>("", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(values),
  });
}

/**
 * Sends an invitation again, with a fresh link and expiry.
 * @param id - the invitation ID
 * @returns the updated invitation
 */
export function resendInvitation(id: string): Promise<Invitation> {
  return send<Invitation>(`/${encodeURIComponent(id)}/resend`, {
    method: "POST",
  });
}

/**
 * Revokes an invitation, disabling the invited user's account.
 * @param id - the invitation ID
 * @returns the updated invitation
 */
export function revokeInvitation(id: string): Promise<Invitation> {
  return send<Invitation>(`/${encodeURIComponent(id)}/revoke`, {
    method: "POST",
  });
}
