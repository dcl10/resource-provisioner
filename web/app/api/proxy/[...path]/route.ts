import type { NextAuthRequest } from "next-auth";

import { auth } from "@/auth";
import {
  buildApiUrl,
  buildDownstreamHeaders,
  buildUpstreamHeaders,
} from "@/lib/api-proxy";
import { isSignedIn } from "@/lib/auth/authorized";

const API_URL = process.env.API_URL ?? "http://localhost:5000";

/**
 * Forwards a browser request to the .NET API with the session's access token as a bearer token, and streams the
 * response back unbuffered, so server-sent events pass straight through. Wrapping it in `auth()` refreshes an
 * expiring token first and writes the refreshed session cookie onto the response.
 */
async function forward(request: NextAuthRequest): Promise<Response> {
  const session = request.auth;
  if (!isSignedIn(session) || !session?.accessToken) {
    return Response.json({ error: "Not signed in" }, { status: 401 });
  }

  const target = buildApiUrl(
    API_URL,
    request.nextUrl.pathname,
    request.nextUrl.search,
  );
  if (!target) return Response.json({ error: "Not found" }, { status: 404 });

  const hasBody = request.method !== "GET" && request.method !== "HEAD";
  let upstream: Response;
  try {
    upstream = await fetch(target, {
      method: request.method,
      headers: buildUpstreamHeaders(request.headers, session.accessToken),
      body: hasBody ? request.body : undefined,
      // Required by Node's fetch to send a streamed request body.
      ...(hasBody ? { duplex: "half" } : {}),
      redirect: "manual",
      cache: "no-store",
      signal: request.signal,
    });
  } catch (error) {
    console.error(`API proxy request to ${target.pathname} failed`, error);
    return Response.json({ error: "The API is unreachable" }, { status: 502 });
  }

  return new Response(upstream.body, {
    status: upstream.status,
    statusText: upstream.statusText,
    headers: buildDownstreamHeaders(upstream.headers),
  });
}

export const GET = auth(forward);
export const POST = auth(forward);
export const PUT = auth(forward);
export const PATCH = auth(forward);
export const DELETE = auth(forward);
