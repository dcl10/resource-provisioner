import { render, screen, within } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { Providers } from "@/components/providers";
import { RequestsTable } from "@/components/requests/requests-table";
import { jsonResponse, testRequest } from "@/lib/test-requests";

function renderTable(isAdmin: boolean) {
  return render(
    <Providers>
      <RequestsTable isAdmin={isAdmin} />
    </Providers>,
  );
}

describe("RequestsTable", () => {
  beforeEach(() => {
    vi.stubGlobal("fetch", async () =>
      jsonResponse([
        testRequest("Applying"),
        testRequest("Destroyed", {
          id: "req-2",
          ownerId: "user-2",
          ownerEmail: "other@example.com",
          variables: { name: "db-1" },
        }),
      ]),
    );
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("lists each request with a link, its name and status", async () => {
    renderTable(false);

    const row = (await screen.findByText("web-1")).closest("tr")!;
    expect(within(row).getByRole("link", { name: "Droplet" })).toHaveAttribute(
      "href",
      "/requests/req-1",
    );
    expect(within(row).getByText("Provisioning")).toBeInTheDocument();
    const other = screen.getByText("db-1").closest("tr")!;
    expect(within(other).getByText("Destroyed")).toBeInTheDocument();
  });

  it("shows the owner column only to admins", async () => {
    const { unmount } = renderTable(false);
    await screen.findByText("web-1");
    expect(
      screen.queryByRole("columnheader", { name: "Owner" }),
    ).not.toBeInTheDocument();
    unmount();

    renderTable(true);
    await screen.findByText("web-1");
    expect(
      screen.getByRole("columnheader", { name: "Owner" }),
    ).toBeInTheDocument();
    expect(screen.getByText("other@example.com")).toBeInTheDocument();
  });

  it("points to the chat when there are no requests", async () => {
    vi.stubGlobal("fetch", async () => jsonResponse([]));
    renderTable(false);

    expect(await screen.findByRole("link", { name: "chat" })).toHaveAttribute(
      "href",
      "/chat",
    );
  });
});
