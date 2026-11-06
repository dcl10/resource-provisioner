// Runs Auth.js's `authorized` callback (lib/auth/authorized.ts) before every page and API proxy request. It also
// refreshes an expiring access token and writes the new session cookie. Route handlers and pages still check the
// session themselves; this is the optimistic first gate.
export { auth as proxy } from "@/auth";

export const config = {
  // Everything except Auth.js's own endpoints and static assets.
  matcher: [
    "/((?!api/auth|_next/static|_next/image|favicon.ico|.*\\.(?:svg|png|jpg|jpeg|gif|webp|ico)$).*)",
  ],
};
