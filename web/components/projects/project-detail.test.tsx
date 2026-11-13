import { render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { ProjectDetail } from "@/components/projects/project-detail";
import { Providers } from "@/components/providers";
import type { ProjectDetail as Project } from "@/lib/projects";
import type { RequestLink } from "@/lib/requests";
import { testProject } from "@/lib/test-projects";
import { jsonResponse, testRequest } from "@/lib/test-requests";

const replace = vi.fn();
const push = vi.fn();
vi.mock("next/navigation", () => ({ useRouter: () => ({ replace, push }) }));

const projectRequest: RequestLink = {
  id: "req-p",
  type: "project",
  displayName: "Project",
  name: "shop",
  status: "Succeeded",
};
const webLink: RequestLink = {
  id: "req-web",
  type: "droplet",
  displayName: "Droplet",
  name: "web-1",
  status: "Succeeded",
};

const withResources = testProject("Succeeded", {
  resourceCount: 2,
  resources: [
    testRequest("Applying", {
      id: "req-fw",
      type: "firewall",
      displayName: "Firewall",
      variables: { name: "fw-1" },
      dependsOn: [projectRequest, webLink],
    }),
    testRequest("Succeeded", {
      id: "req-web",
      dependsOn: [projectRequest],
    }),
  ],
});

function mockApi(respond: () => Response) {
  vi.stubGlobal(
    "fetch",
    vi.fn(async () => respond()),
  );
}

function renderDetail(user = { id: "user-1", isAdmin: false }) {
  return render(
    <Providers>
      <ProjectDetail projectId="prj-1" user={user} />
    </Providers>,
  );
}

describe("ProjectDetail", () => {
  beforeEach(() => {
    replace.mockReset();
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("shows the project's status and settings, and links its request", async () => {
    mockApi(() => jsonResponse(testProject("Succeeded")));
    renderDetail();

    expect(
      await screen.findByRole("heading", { name: "shop" }),
    ).toBeInTheDocument();
    expect(screen.getByText("Provisioned")).toBeInTheDocument();
    expect(screen.getByText("The online shop")).toBeInTheDocument();
    expect(screen.getByText("Web Application")).toBeInTheDocument();
    expect(screen.getByText("Development")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "its request" })).toHaveAttribute(
      "href",
      "/requests/req-p",
    );
  });

  it("nests each resource under what it depends on, linking to it", async () => {
    mockApi(() => jsonResponse(withResources));
    renderDetail();

    const top = await screen.findByRole("list", { name: "Resources" });
    const droplet = within(top).getByRole("link", { name: "Droplet" });
    expect(droplet).toHaveAttribute("href", "/requests/req-web");

    const nested = within(top).getByRole("list", {
      name: "Depends on Droplet web-1",
    });
    expect(
      within(nested).getByRole("link", { name: "Firewall" }),
    ).toHaveAttribute("href", "/requests/req-fw");
    expect(within(nested).getByText("fw-1")).toBeInTheDocument();
    expect(within(nested).getByText("Provisioning")).toBeInTheDocument();
  });

  it("offers the owner a chat to add resources", async () => {
    mockApi(() => jsonResponse(testProject("Succeeded")));
    renderDetail();

    expect(
      await screen.findByRole("button", { name: "Start a chat" }),
    ).toBeInTheDocument();
    expect(screen.getByText(/Nothing in this project yet/)).toBeInTheDocument();
  });

  it("shows an admin whose project it is, without the chat", async () => {
    mockApi(() => jsonResponse(testProject("Succeeded")));
    renderDetail({ id: "admin-1", isAdmin: true });

    expect(
      await screen.findByText(/Owner: user@example\.com/),
    ).toBeInTheDocument();
    expect(
      screen.queryByRole("button", { name: "Start a chat" }),
    ).not.toBeInTheDocument();
  });

  it("explains a project that doesn't exist", async () => {
    mockApi(() =>
      jsonResponse({ detail: "Project prj-1 doesn't exist." }, 404),
    );
    renderDetail();

    expect(await screen.findByRole("alert")).toHaveTextContent(
      "This project doesn't exist, or isn't yours.",
    );
    expect(replace).not.toHaveBeenCalled();
  });

  it("goes back to the projects list once the project is deleted", async () => {
    let current: Project | null = testProject("Destroying");
    vi.stubGlobal(
      "fetch",
      vi.fn(async () => {
        const response = current
          ? jsonResponse(current)
          : jsonResponse({ detail: "Project prj-1 doesn't exist." }, 404);
        // The runner finishes the destroy before the next poll.
        current = null;
        return response;
      }),
    );
    renderDetail();

    await screen.findByRole("heading", { name: "shop" });
    await waitFor(() => expect(replace).toHaveBeenCalledWith("/projects"), {
      timeout: 5000,
    });
    expect(
      screen.getByText(
        "Project shop no longer exists. Taking you back to your projects…",
      ),
    ).toBeInTheDocument();
  }, 10_000);
});
