import { Boxes } from "lucide-react";
import Link from "next/link";
import type { ReactNode } from "react";

import { MainNav } from "@/components/main-nav";
import { ThemeToggle } from "@/components/theme-toggle";

/**
 * Page chrome: a header with the app name, main navigation and theme toggle, above the page content.
 * @param children - the page
 * @param isAdmin - whether to show admin-only navigation (false until sign-in lands in #7)
 * @returns the shell around the page
 */
export function AppShell({
  children,
  isAdmin = false,
}: {
  children: ReactNode;
  isAdmin?: boolean;
}) {
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
          <MainNav isAdmin={isAdmin} />
          <div className="ml-auto flex items-center gap-1">
            <ThemeToggle />
          </div>
        </div>
      </header>
      <main className="mx-auto flex w-full max-w-6xl flex-1 flex-col px-4 py-6">
        {children}
      </main>
    </div>
  );
}
