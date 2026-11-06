import { render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";

import { AppShell } from "@/components/app-shell";
import { Providers } from "@/components/providers";

vi.mock("next/navigation", () => ({
  usePathname: () => "/chat",
}));

// The real ones read the session and call Auth.js on the server.
vi.mock("@/components/session-header-nav", () => ({
  SessionHeaderNav: () => <nav aria-label="Session nav" />,
}));
vi.mock("@/lib/auth/actions", () => ({ signOutEverywhere: vi.fn() }));

describe("AppShell", () => {
  it("renders the page and the session-dependent header", () => {
    render(
      <Providers>
        <AppShell>
          <p>Page content</p>
        </AppShell>
      </Providers>,
    );

    expect(screen.getByText("Page content")).toBeInTheDocument();
    expect(
      screen.getByRole("navigation", { name: "Session nav" }),
    ).toBeInTheDocument();
  });
});
