import { render, screen, within } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { ProjectsTable } from "@/components/projects/projects-table";
import { Providers } from "@/components/providers";
import { testProject } from "@/lib/test-projects";
import { jsonResponse } from "@/lib/test-requests";

function renderTable(isAdmin: boolean) {
  return render(
    <Providers>
      <ProjectsTable isAdmin={isAdmin} />
    </Providers>,
  );
}

describe("ProjectsTable", () => {
  beforeEach(() => {
    vi.stubGlobal("fetch", async () =>
      jsonResponse([
        testProject("Succeeded", { resourceCount: 3 }),
        testProject("Applying", {
          id: "prj-2",
          name: "blog",
          ownerId: "user-2",
          ownerEmail: "other@example.com",
          environment: "Production",
        }),
      ]),
    );
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("lists each project with a link, its status, environment and resource count", async () => {
    renderTable(false);

    const link = await screen.findByRole("link", { name: "shop" });
    expect(link).toHaveAttribute("href", "/projects/prj-1");
    const row = link.closest("tr")!;
    expect(within(row).getByText("Provisioned")).toBeInTheDocument();
    expect(within(row).getByText("Development")).toBeInTheDocument();
    expect(within(row).getByText("3")).toBeInTheDocument();
    const other = screen.getByRole("link", { name: "blog" }).closest("tr")!;
    expect(within(other).getByText("Provisioning")).toBeInTheDocument();
  });

  it("shows the owner column only to admins", async () => {
    const { unmount } = renderTable(false);
    await screen.findByText("shop");
    expect(
      screen.queryByRole("columnheader", { name: "Owner" }),
    ).not.toBeInTheDocument();
    unmount();

    renderTable(true);
    await screen.findByText("shop");
    expect(
      screen.getByRole("columnheader", { name: "Owner" }),
    ).toBeInTheDocument();
    expect(screen.getByText("other@example.com")).toBeInTheDocument();
  });

  it("says when there are no projects", async () => {
    vi.stubGlobal("fetch", async () => jsonResponse([]));
    renderTable(false);

    expect(await screen.findByText(/No projects yet/)).toBeInTheDocument();
  });
});
