import { apiFetch } from "@/lib/api";

/** Where a provisioning request has got to. */
export type RequestStatus =
  | "AwaitingConfirmation"
  | "Queued"
  | "Applying"
  | "Succeeded"
  | "Failed"
  | "Destroying"
  | "Destroyed"
  | "Cancelled";

/** A provisioning request as `GET /api/requests/{id}` returns it. Never holds secrets. Dates are ISO 8601. */
export interface ProvisioningRequest {
  id: string;
  ownerId: string;
  ownerEmail: string | null;
  type: string;
  displayName: string;
  status: RequestStatus;
  variables: Record<string, unknown>;
  outputs: Record<string, unknown> | null;
  conversationId: string | null;
  createdAt: string;
  updatedAt: string;
  confirmedAt: string | null;
}

/** A short, human label for each status. */
export const statusLabels: Record<RequestStatus, string> = {
  AwaitingConfirmation: "Awaiting confirmation",
  Queued: "Queued",
  Applying: "Provisioning",
  Succeeded: "Provisioned",
  Failed: "Failed",
  Destroying: "Destroying",
  Destroyed: "Destroyed",
  Cancelled: "Cancelled",
};

/**
 * The detail page of a request.
 * @param id - the request ID
 * @returns its path
 */
export function requestPath(id: string): string {
  return `/requests/${encodeURIComponent(id)}`;
}

/**
 * Reads one request.
 * @param id - the request ID
 * @returns the request
 */
export function getRequest(id: string): Promise<ProvisioningRequest> {
  return apiFetch<ProvisioningRequest>(`/requests/${encodeURIComponent(id)}`);
}

/**
 * Confirms a proposed request: the only thing that starts provisioning. Fails with 409 over an active-request limit,
 * or 422 if its variables are no longer valid.
 * @param id - the request ID
 * @returns the request, now Queued
 */
export function confirmRequest(id: string): Promise<ProvisioningRequest> {
  return apiFetch<ProvisioningRequest>(
    `/requests/${encodeURIComponent(id)}/confirm`,
    { method: "POST" },
  );
}

/**
 * Cancels a proposed request before it's confirmed.
 * @param id - the request ID
 * @returns the request, now Cancelled
 */
export function cancelRequest(id: string): Promise<ProvisioningRequest> {
  return apiFetch<ProvisioningRequest>(
    `/requests/${encodeURIComponent(id)}/cancel`,
    { method: "POST" },
  );
}
