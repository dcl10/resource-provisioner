import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";

import { HeaderNav } from "@/components/header-nav";
import { Providers } from "@/components/providers";
import type { SessionUser } from "@/lib/auth/types";

vi.mock("next/navigation", () => ({
  usePathname: () => "/requests",
}));

const { signOutEverywhere } = vi.hoisted(() => ({
  signOutEverywhere: vi.fn(),
}));
vi.mock("@/lib/auth/actions", () => ({ signOutEverywhere }));

const ada: SessionUser = {
  id: "user-1",
  name: "Ada Lovelace",
  email: "ada@example.com",
  isAdmin: false,
};

function renderNav(user?: SessionUser | null) {
  return render(
    <Providers>
      <HeaderNav user={user} />
    </Providers>,
  );
}

describe("HeaderNav", () => {
  it("renders the Chat, Projects and Requests links with the current one marked", () => {
    renderNav(ada);

    expect(screen.getByRole("link", { name: "Chat" })).toHaveAttribute(
      "href",
      "/chat",
    );
    expect(screen.getByRole("link", { name: "Projects" })).toHaveAttribute(
      "href",
      "/projects",
    );
    expect(screen.getByRole("link", { name: "Requests" })).toHaveAttribute(
      "aria-current",
      "page",
    );
  });

  it("hides the Users link when the user is not an admin", () => {
    renderNav(ada);

    expect(
      screen.queryByRole("link", { name: "Users" }),
    ).not.toBeInTheDocument();
  });

  it("shows the Users link when the user is an admin", () => {
    renderNav({ ...ada, isAdmin: true });

    expect(screen.getByRole("link", { name: "Users" })).toBeInTheDocument();
  });

  it("hides the Users link and the account menu while the session loads", () => {
    renderNav(undefined);

    expect(
      screen.queryByRole("link", { name: "Users" }),
    ).not.toBeInTheDocument();
    expect(
      screen.queryByRole("button", { name: "Account" }),
    ).not.toBeInTheDocument();
  });

  it("shows the user's name and email and signs out from the account menu", async () => {
    const user = userEvent.setup();
    renderNav(ada);

    await user.click(screen.getByRole("button", { name: "Account" }));

    expect(await screen.findByText("Ada Lovelace")).toBeInTheDocument();
    expect(screen.getByText("ada@example.com")).toBeInTheDocument();

    await user.click(screen.getByRole("menuitem", { name: "Sign out" }));

    expect(signOutEverywhere).toHaveBeenCalledOnce();
  });
});
