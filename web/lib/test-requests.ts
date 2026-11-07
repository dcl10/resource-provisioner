import type { ProvisioningRequest, RequestStatus } from "@/lib/requests";

/**
 * A provisioning request for tests: a droplet named web-1 owned by user-1.
 * @param status - its status
 * @param overrides - fields to change
 * @returns the request
 */
export function testRequest(
  status: RequestStatus,
  overrides: Partial<ProvisioningRequest> = {},
): ProvisioningRequest {
  return {
    id: "req-1",
    ownerId: "user-1",
    ownerEmail: "user@example.com",
    type: "droplet",
    displayName: "Droplet",
    status,
    variables: {
      name: "web-1",
      region: "lon1",
      ssh_public_key: "ssh-ed25519 AAAA",
    },
    outputs: status === "Succeeded" ? { ipv4_address: "192.0.2.1" } : null,
    conversationId: null,
    createdAt: "2026-11-07T10:00:00Z",
    updatedAt: "2026-11-07T10:00:00Z",
    confirmedAt: "2026-11-07T10:01:00Z",
    ...overrides,
  };
}

/**
 * A JSON response, for mocked fetch.
 * @param body - the body
 * @param status - the HTTP status
 * @returns the response
 */
export function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": "application/json" },
  });
}
