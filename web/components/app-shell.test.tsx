import { render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";

import { AppShell } from "@/components/app-shell";
import { Providers } from "@/components/providers";

vi.mock("next/navigation", () => ({
  usePathname: () => "/requests",
}));

function renderShell(isAdmin: boolean) {
  return render(
    <Providers>
      <AppShell isAdmin={isAdmin}>
        <p>Page content</p>
      </AppShell>
    </Providers>,
  );
}

describe("AppShell", () => {
  it("renders the page with Chat and Requests links", () => {
    renderShell(false);

    expect(screen.getByText("Page content")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Chat" })).toHaveAttribute(
      "href",
      "/chat",
    );
    expect(screen.getByRole("link", { name: "Requests" })).toHaveAttribute(
      "aria-current",
      "page",
    );
  });

  it("hides the Users link when the user is not an admin", () => {
    renderShell(false);

    expect(
      screen.queryByRole("link", { name: "Users" }),
    ).not.toBeInTheDocument();
  });

  it("shows the Users link when the user is an admin", () => {
    renderShell(true);

    expect(screen.getByRole("link", { name: "Users" })).toBeInTheDocument();
  });
});
