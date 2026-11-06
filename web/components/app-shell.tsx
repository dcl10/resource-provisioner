import { Boxes } from "lucide-react";
import Link from "next/link";
import { type ReactNode, Suspense } from "react";

import { HeaderNav } from "@/components/header-nav";
import { SessionHeaderNav } from "@/components/session-header-nav";

/**
 * Page chrome for signed-in pages: a header with the app name, main navigation, theme toggle and account menu, above
 * the page content. Only the session-dependent part of the header waits for the session, behind its own `<Suspense>`
 * boundary, so the page itself isn't held back by it.
 * @param children - the page
 * @returns the shell around the page
 */
export function AppShell({ children }: { children: ReactNode }) {
  return (
    <div className="flex min-h-full flex-col">
      <header className="sticky top-0 z-10 border-b bg-background/80 backdrop-blur">
        <div className="mx-auto flex h-14 w-full max-w-6xl items-center gap-4 px-4">
          <Link
            href="/chat"
            className="flex items-center gap-2 font-semibold tracking-tight"
          >
            <Boxes className="size-5" />
            <span className="hidden md:inline">Resource Provisioner</span>
          </Link>
          <Suspense fallback={<HeaderNav />}>
            <SessionHeaderNav />
          </Suspense>
        </div>
      </header>
      <main className="mx-auto flex w-full max-w-6xl flex-1 flex-col px-4 py-6">
        {children}
      </main>
    </div>
  );
}
