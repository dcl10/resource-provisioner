"use client";

import { LogOut } from "lucide-react";
import { useTransition } from "react";

import { Avatar, AvatarFallback } from "@/components/ui/avatar";
import { Button } from "@/components/ui/button";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuGroup,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import { signOutEverywhere } from "@/lib/auth/actions";
import type { SessionUser } from "@/lib/auth/types";

/**
 * Up to two initials for the avatar, from the name or else the email address.
 * @param user - the signed-in user
 * @returns the initials, or "?" when there's nothing to take them from
 */
export function getInitials(user: Pick<SessionUser, "name" | "email">): string {
  const words = (user.name ?? "").trim().split(/\s+/).filter(Boolean);
  if (words.length > 0) {
    return words
      .slice(0, 2)
      .map((word) => word[0])
      .join("")
      .toUpperCase();
  }
  return user.email?.[0]?.toUpperCase() ?? "?";
}

/**
 * The header's account menu: the user's name and email, and sign out (which also ends the Keycloak session).
 * @param user - the signed-in user
 * @returns the avatar button and its menu
 */
export function UserMenu({ user }: { user: SessionUser }) {
  const [signingOut, startSignOut] = useTransition();
  const displayName = user.name ?? user.email ?? "Signed in";

  return (
    <DropdownMenu>
      <DropdownMenuTrigger
        render={
          <Button
            variant="ghost"
            size="icon"
            className="rounded-full"
            aria-label="Account"
          />
        }
      >
        <Avatar className="size-7">
          <AvatarFallback className="text-xs">
            {getInitials(user)}
          </AvatarFallback>
        </Avatar>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end" className="min-w-56">
        <DropdownMenuGroup>
          <DropdownMenuLabel className="flex flex-col gap-0.5">
            <span className="text-sm font-medium text-foreground">
              {displayName}
            </span>
            {user.email && user.email !== displayName && (
              <span className="truncate">{user.email}</span>
            )}
          </DropdownMenuLabel>
        </DropdownMenuGroup>
        <DropdownMenuSeparator />
        <DropdownMenuItem
          disabled={signingOut}
          onClick={() => startSignOut(() => signOutEverywhere())}
        >
          <LogOut /> Sign out
        </DropdownMenuItem>
      </DropdownMenuContent>
    </DropdownMenu>
  );
}
