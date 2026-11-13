import { API_BASE, ApiError, apiFetch, readApiError } from "@/lib/api";
import { SseParser } from "@/lib/sse";

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

/** A short reference to another request: one a request depends on, or one that depends on it. */
export interface RequestLink {
  id: string;
  type: string;
  displayName: string;
  /** The resource's `name` variable, if it has one. */
  name: string | null;
  status: RequestStatus;
}

/** A short reference to a project: the one a request belongs to, or backs. */
export interface ProjectLink {
  id: string;
  name: string;
}

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
  /** The requests it references, which are provisioned before it. */
  dependsOn: RequestLink[];
  /** The requests that reference it, which must be destroyed before it. */
  dependents: RequestLink[];
}

/** Statuses in which the runner has a job queued or running, so the request will change by itself. */
export const inProgressStatuses: readonly RequestStatus[] = [
  "Queued",
  "Applying",
  "Destroying",
];

/**
 * Whether the runner still has work to do on a request in this status.
 * @param status - the status
 * @returns whether it's queued, applying or destroying
 */
export function isInProgress(status: RequestStatus): boolean {
  return inProgressStatuses.includes(status);
}

/**
 * Whether a request in this status can be destroyed: once provisioned, or after a failed apply or destroy (which
 * may have left something behind).
 * @param status - the status
 * @returns whether destroy is allowed
 */
export function canDestroy(status: RequestStatus): boolean {
  return status === "Succeeded" || status === "Failed";
}

/** Statuses of a dependent that stop what it depends on being destroyed: it exists, may exist, or is about to. */
export const blockingDependentStatuses: readonly RequestStatus[] = [
  "Queued",
  "Applying",
  "Succeeded",
  "Failed",
  "Destroying",
];

/**
 * The requests that must be destroyed before this one can be.
 * @param request - the request
 * @returns its dependents that exist, may exist, or are about to
 */
export function blockingDependents(
  request: ProvisioningRequest,
): RequestLink[] {
  return request.dependents.filter((d) =>
    blockingDependentStatuses.includes(d.status),
  );
}

/**
 * The requests a queued request is waiting for: what it depends on that hasn't been provisioned yet.
 * @param request - the request
 * @returns the dependencies still to succeed; empty unless it's queued
 */
export function waitingFor(request: ProvisioningRequest): RequestLink[] {
  return request.status === "Queued"
    ? request.dependsOn.filter((d) => d.status !== "Succeeded")
    : [];
}

/**
 * How the UI names a linked request: its type and, if it has one, its name.
 * @param link - the request
 * @returns such as `Droplet web-1`
 */
export function linkLabel(link: RequestLink): string {
  return link.name ? `${link.displayName} ${link.name}` : link.displayName;
}

/**
 * The name the user gave the resource, if its type has a `name` variable.
 * @param request - the request
 * @returns the name, or null
 */
export function requestName(request: ProvisioningRequest): string | null {
  const name = request.variables.name;
  return typeof name === "string" ? name : null;
}

/**
 * A variable or output value for display: strings as they are, anything else as JSON.
 * @param value - the value
 * @returns the text to show
 */
export function formatValue(value: unknown): string {
  if (value === null || value === undefined) return "—";
  return typeof value === "string" ? value : JSON.stringify(value);
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
 * Confirms several proposed requests at once, all or none: the spend limits are checked for the whole set, and they're
 * provisioned in dependency order.
 * @param ids - the request IDs
 * @returns the requests, now Queued, in the order given
 */
export function confirmRequests(ids: string[]): Promise<ProvisioningRequest[]> {
  return apiFetch<ProvisioningRequest[]>("/requests/confirm", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ requestIds: ids }),
  });
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

/**
 * Lists the user's requests, or everyone's for an admin, newest first.
 * @returns the requests
 */
export function listRequests(): Promise<ProvisioningRequest[]> {
  return apiFetch<ProvisioningRequest[]>("/requests");
}

/**
 * Destroys what a request created: queues `terraform destroy`. Allowed from Succeeded or Failed, for the owner or an
 * admin.
 * @param id - the request ID
 * @returns the request, now Destroying
 */
export function destroyRequest(id: string): Promise<ProvisioningRequest> {
  return apiFetch<ProvisioningRequest>(
    `/requests/${encodeURIComponent(id)}/destroy`,
    { method: "POST" },
  );
}

/**
 * Reveals a request's secret outputs (passwords, keys, connection URIs). Owner only; never cached.
 * @param id - the request ID
 * @returns the secret outputs by name; empty if there are none
 */
export function getSensitiveOutputs(
  id: string,
): Promise<Record<string, unknown>> {
  return apiFetch<Record<string, unknown>>(
    `/requests/${encodeURIComponent(id)}/sensitive-outputs`,
    { cache: "no-store" },
  );
}

/** Where a log line came from. */
export type LogStream = "System" | "Stdout" | "Stderr";

/** One line of a request's Terraform log. */
export interface LogLine {
  id: number;
  at: string;
  stream: LogStream;
  text: string;
}

/** Callbacks for {@link followLogs}. */
export interface LogHandlers {
  /** A log line, oldest first. */
  onLine: (line: LogLine) => void;
  /** The request's status: first, then whenever it changes. */
  onStatus: (status: RequestStatus) => void;
}

/**
 * Follows a request's Terraform log (`GET /api/requests/{id}/logs`, server-sent events): every line after `afterId`,
 * then new lines and status changes as the runner writes them. Resolves once no job is queued or running.
 * @param id - the request ID
 * @param afterId - the last line already shown; 0 for all of them
 * @param handlers - called for each line and status
 * @param signal - stops following
 * @returns the status the request ended in
 */
export async function followLogs(
  id: string,
  afterId: number,
  handlers: LogHandlers,
  signal?: AbortSignal,
): Promise<RequestStatus> {
  const response = await fetch(
    `${API_BASE}/requests/${encodeURIComponent(id)}/logs`,
    {
      headers: {
        Accept: "text/event-stream",
        ...(afterId > 0 ? { "Last-Event-ID": String(afterId) } : {}),
      },
      cache: "no-store",
      signal,
    },
  );
  if (!response.ok) throw await readApiError(response);
  if (!response.body) throw new ApiError("The log couldn't be read.", 0);

  const parser = new SseParser();
  const reader = response.body.pipeThrough(new TextDecoderStream()).getReader();
  for (;;) {
    const { value, done } = await reader.read();
    if (done) break;
    for (const message of parser.push(value)) {
      const data = JSON.parse(message.data) as LogLine & {
        status?: RequestStatus;
      };
      if (message.event === "log") handlers.onLine(data);
      else if (message.event === "status" && data.status)
        handlers.onStatus(data.status);
      else if (message.event === "end" && data.status) return data.status;
    }
  }

  throw new ApiError("The log stream ended unexpectedly.", 0);
}
