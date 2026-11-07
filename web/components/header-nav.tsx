import { Suspense } from "react";

import { MainNav, MainNavLinks } from "@/components/main-nav";
import { ThemeToggle } from "@/components/theme-toggle";
import { Skeleton } from "@/components/ui/skeleton";
import { UserMenu } from "@/components/user-menu";
import type { SessionUser } from "@/lib/auth/types";

/**
 * The session-dependent part of the header: main navigation, theme toggle and account menu.
 * @param user - the signed-in user; `undefined` while the session is loading shows a placeholder for the menu
 * @returns the navigation and the controls on the right of the header
 */
export function HeaderNav({ user }: { user?: SessionUser | null }) {
  return (
    <>
      {/* The highlight needs the URL, unknown until request time on dynamic routes such as /chat/[id]. */}
      <Suspense fallback={<MainNavLinks isAdmin={user?.isAdmin ?? false} />}>
        <MainNav isAdmin={user?.isAdmin ?? false} />
      </Suspense>
      <div className="ml-auto flex items-center gap-1">
        <ThemeToggle />
        {user === undefined ? (
          <Skeleton className="size-7 rounded-full" />
        ) : (
          user && <UserMenu user={user} />
        )}
      </div>
    </>
  );
}
