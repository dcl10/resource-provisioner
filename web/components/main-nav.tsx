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
 * The main navigation links, with the current section highlighted.
 * @param isAdmin - whether to show admin-only links
 * @returns the nav bar
 */
export function MainNav({ isAdmin }: { isAdmin: boolean }) {
  const pathname = usePathname();

  return (
    <nav aria-label="Main" className="flex items-center gap-1">
      {navItems
        .filter((item) => isAdmin || !item.adminOnly)
        .map(({ href, label, icon: Icon }) => {
          const active = pathname === href || pathname.startsWith(`${href}/`);

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
