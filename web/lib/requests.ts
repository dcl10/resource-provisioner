import { API_BASE, ApiError, apiFetch, readApiError } from "@/lib/api";
import { SseParser } from "@/lib/sse";

/**
 * Where a provisioning request has got to. A cancelled or destroyed request is deleted, so there's no status for
 * either: the API answers 404 for it from then on.
 */
export type RequestStatus =
  | "AwaitingConfirmation"
  | "Queued"
  | "Applying"
  | "Succeeded"
  | "Failed"
  | "Destroying"
  | "DestroyFailed";

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
  /** The project it belongs to; for a `project` request, the project it backs. Null only for one from before projects. */
  project: ProjectLink | null;
  conversationId: string | null;
  createdAt: string;
  updatedAt: string;
  confirmedAt: string | null;
  /** The requests it references, which are provisioned before it. */
  dependsOn: RequestLink[];
  /** The requests that depend on it (reference it, or are in the project it backs): destroying it destroys them first. */
  dependents: RequestLink[];
  /**
   * When its destroy stopped because something that depends on it failed to destroy, and it went back to the status it
   * had: that request, while it still exists. Null otherwise.
   */
  destroyStoppedBy: RequestLink | null;
}

/**
 * What destroying a request would do, as `GET /api/requests/{id}/destroy-plan` returns it. The destroy itself works it
 * out again, so it may differ if something changed in between.
 */
export interface DestroyPlan {
  /** Whether a destroy would be accepted now. */
  canDestroy: boolean;
  /** Why not, in the API's words; null when it can. */
  reason: string | null;
  /** What would be destroyed, in teardown order: each before anything it depends on, so the request itself is last. */
  destroy: RequestLink[];
  /** The proposals that depend on it, which would be cancelled (deleted). */
  cancel: RequestLink[];
  /** What depends on it and is still queued, provisioning or being destroyed: a destroy waits for these to finish. */
  waitFor: RequestLink[];
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
 * may have left something behind). Destroying it destroys what depends on it too; its destroy plan says what that is,
 * and whether something still running makes it wait.
 * @param status - the status
 * @returns whether destroy is allowed
 */
export function canDestroy(status: RequestStatus): boolean {
  return (
    status === "Succeeded" || status === "Failed" || status === "DestroyFailed"
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
  DestroyFailed: "Destroy failed",
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
 * Cancels a proposed request before it's confirmed. Nothing was created, so the API deletes it: reading it afterwards
 * answers 404.
 * @param id - the request ID
 * @returns once it's deleted
 */
export function cancelRequest(id: string): Promise<void> {
  return apiFetch<void>(`/requests/${encodeURIComponent(id)}/cancel`, {
    method: "POST",
  });
}

/**
 * The requests in a project, with the `project` request that backs it: those whose project it is.
 * @param requests - the requests
 * @param projectId - the project, or null for every request
 * @returns the matching requests, in the same order
 */
export function inProject(
  requests: ProvisioningRequest[],
  projectId: string | null,
): ProvisioningRequest[] {
  return projectId === null
    ? requests
    : requests.filter((r) => r.project?.id === projectId);
}

/**
 * Lists the user's requests, or everyone's for an admin, newest first.
 * @returns the requests
 */
export function listRequests(): Promise<ProvisioningRequest[]> {
  return apiFetch<ProvisioningRequest[]>("/requests");
}

/**
 * What destroying a request would do: what's destroyed, in order, what's cancelled, and whether it can go ahead now.
 * @param id - the request ID
 * @returns the plan
 */
export function getDestroyPlan(id: string): Promise<DestroyPlan> {
  return apiFetch<DestroyPlan>(
    `/requests/${encodeURIComponent(id)}/destroy-plan`,
  );
}

/**
 * Destroys what a request created, and everything that depends on it: cancels the proposals among them and queues
 * `terraform destroy` for the rest, in teardown order. Allowed from Succeeded, Failed or DestroyFailed, for the owner
 * or an admin; 409 while something that depends on it is still running.
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
 * then new lines and status changes as the runner writes them. Resolves once no job is queued or running, or once
 * the request has been destroyed and so deleted, log and all.
 * @param id - the request ID
 * @param afterId - the last line already shown; 0 for all of them
 * @param handlers - called for each line and status
 * @param signal - stops following
 * @returns the status the request ended in, or null if it was deleted
 */
export async function followLogs(
  id: string,
  afterId: number,
  handlers: LogHandlers,
  signal?: AbortSignal,
): Promise<RequestStatus | null> {
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
      else if (message.event === "deleted") return null;
    }
  }

  throw new ApiError("The log stream ended unexpectedly.", 0);
}
