import { describe, expect, it, vi } from "vitest";

import { AdminOnly } from "@/components/admin/admin-only";
import type { SessionUser } from "@/lib/auth/types";

const { getCurrentUser, redirect } = vi.hoisted(() => ({
  getCurrentUser: vi.fn<() => Promise<SessionUser | null>>(),
  redirect: vi.fn((url: string) => {
    throw new Error(`NEXT_REDIRECT ${url}`);
  }),
}));
vi.mock("@/lib/auth/session", () => ({ getCurrentUser }));
vi.mock("next/navigation", () => ({ redirect }));

const user: SessionUser = {
  id: "user-1",
  name: "Ada",
  email: "ada@example.com",
  isAdmin: false,
};

describe("AdminOnly", () => {
  it("renders its children when the user is an admin", async () => {
    getCurrentUser.mockResolvedValue({ ...user, isAdmin: true });

    await expect(AdminOnly({ children: "secret" })).resolves.toBe("secret");
  });

  it("redirects to the chat when the user is not an admin", async () => {
    getCurrentUser.mockResolvedValue(user);

    await expect(AdminOnly({ children: "secret" })).rejects.toThrow(
      "NEXT_REDIRECT /chat",
    );
  });

  it("redirects to the chat when nobody is signed in", async () => {
    getCurrentUser.mockResolvedValue(null);

    await expect(AdminOnly({ children: "secret" })).rejects.toThrow(
      "NEXT_REDIRECT /chat",
    );
  });
});
