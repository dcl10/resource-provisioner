import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { Providers } from "@/components/providers";
import { RequestsTable } from "@/components/requests/requests-table";
import { jsonResponse, testRequest } from "@/lib/test-requests";

const replace = vi.fn();
vi.mock("next/navigation", () => ({ useRouter: () => ({ replace }) }));

const shop = { id: "prj-1", name: "shop" };

const projects = [
  {
    ...shop,
    ownerId: "user-1",
    ownerEmail: "user@example.com",
    description: null,
    purpose: null,
    environment: "Development",
    status: "Succeeded",
    projectRequestId: "req-p",
    resourceCount: 1,
    createdAt: "2026-11-07T09:00:00Z",
  },
];

function renderTable(isAdmin: boolean, projectId: string | null = null) {
  return render(
    <Providers>
      <RequestsTable isAdmin={isAdmin} projectId={projectId} />
    </Providers>,
  );
}

describe("RequestsTable", () => {
  beforeEach(() => {
    replace.mockReset();
    vi.stubGlobal("fetch", async (url: string) =>
      url.endsWith("/projects")
        ? jsonResponse(projects)
        : jsonResponse([
            testRequest("Applying", { project: shop }),
            testRequest("Failed", {
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
    expect(within(other).getByText("Failed")).toBeInTheDocument();
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

  it("links each request's project", async () => {
    renderTable(false);

    const row = (await screen.findByText("web-1")).closest("tr")!;
    expect(within(row).getByRole("link", { name: "shop" })).toHaveAttribute(
      "href",
      "/projects/prj-1",
    );
  });

  it("shows only the chosen project's requests", async () => {
    renderTable(false, "prj-1");

    expect(await screen.findByText("web-1")).toBeInTheDocument();
    expect(screen.queryByText("db-1")).not.toBeInTheDocument();
  });

  it("filters by project through the URL", async () => {
    const user = userEvent.setup();
    renderTable(false);
    await screen.findByText("web-1");

    await user.click(screen.getByRole("combobox", { name: "Project" }));
    await user.click(await screen.findByRole("option", { name: "shop" }));

    expect(replace).toHaveBeenCalledWith("/requests?project=prj-1");
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
