import type { NextRequest } from "next/server";

import { handlers } from "@/auth";
import { toPublicSession } from "@/lib/auth/public-session";

export const { POST } = handlers;

/**
 * Auth.js's GET endpoints. `/api/auth/session` is filtered through `toPublicSession`, because the session callback
 * adds the Keycloak tokens for server code and they must not reach the browser.
 */
export async function GET(request: NextRequest): Promise<Response> {
  const response = await handlers.GET(request);
  if (!request.nextUrl.pathname.endsWith("/session") || !response.ok) {
    return response;
  }

  const headers = new Headers(response.headers);
  headers.delete("content-length");
  return Response.json(toPublicSession(await response.json()), {
    status: response.status,
    headers,
  });
}
