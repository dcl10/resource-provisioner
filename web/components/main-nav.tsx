"use client";

import { type LucideIcon, MessageSquare, Server, Users } from "lucide-react";
import Link from "next/link";
import { usePathname } from "next/navigation";

import { cn } from "@/lib/utils";

/** A link in the main navigation. */
export interface NavItem {
  href: string;
  label: string;
  icon: LucideIcon;
  /** Only shown to users with the `rp-admin` role. */
  adminOnly?: boolean;
}

/** Every page in the main navigation, in display order. */
export const navItems: NavItem[] = [
  { href: "/chat", label: "Chat", icon: MessageSquare },
  { href: "/requests", label: "Requests", icon: Server },
  { href: "/admin/users", label: "Users", icon: Users, adminOnly: true },
];

/**
 * The main navigation links, with the current section highlighted. Reads the URL, which on a route with a dynamic
 * segment (such as `/chat/[id]`) is only known per request, so it suspends there: render it inside a `<Suspense>`
 * boundary whose fallback is {@link MainNavLinks} with no `pathname`.
 * @param isAdmin - whether to show admin-only links
 * @returns the nav bar
 */
export function MainNav({ isAdmin }: { isAdmin: boolean }) {
  return <MainNavLinks isAdmin={isAdmin} pathname={usePathname()} />;
}

/**
 * The main navigation links, highlighting the section `pathname` is in. Doesn't read the URL itself.
 * @param isAdmin - whether to show admin-only links
 * @param pathname - the current path; with none, no link is highlighted
 * @returns the nav bar
 */
export function MainNavLinks({
  isAdmin,
  pathname,
}: {
  isAdmin: boolean;
  pathname?: string;
}) {
  return (
    <nav aria-label="Main" className="flex items-center gap-1">
      {navItems
        .filter((item) => isAdmin || !item.adminOnly)
        .map(({ href, label, icon: Icon }) => {
          const active =
            pathname !== undefined &&
            (pathname === href || pathname.startsWith(`${href}/`));

          return (
            <Link
              key={href}
              href={href}
              aria-current={active ? "page" : undefined}
              className={cn(
                "flex items-center gap-2 rounded-md px-3 py-1.5 text-sm font-medium text-muted-foreground transition-colors hover:bg-muted hover:text-foreground",
                active && "bg-muted text-foreground",
              )}
            >
              <Icon className="size-4" />
              <span className="hidden sm:inline">{label}</span>
            </Link>
          );
        })}
    </nav>
  );
}
