import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { Providers } from "@/components/providers";
import { RequestDetail } from "@/components/requests/request-detail";
import type { ProvisioningRequest } from "@/lib/requests";
import { jsonResponse, testRequest } from "@/lib/test-requests";
import { sseEvent, sseResponse } from "@/lib/test-sse";

const replace = vi.fn();
vi.mock("next/navigation", () => ({ useRouter: () => ({ replace }) }));

const catalog = [
  {
    id: "droplet",
    displayName: "Droplet",
    description: "A server.",
    costHint: "About $4 a month.",
    outputs: [
      { name: "ipv4_address", description: "Public IPv4 address." },
      { name: "root_password", description: "Root password.", sensitive: true },
    ],
  },
];

let fetchMock: ReturnType<typeof vi.fn>;

function mockApi(request: ProvisioningRequest) {
  fetchMock = vi.fn(async (url: string) => {
    if (url.endsWith("/logs")) {
      return sseResponse([
        sseEvent("status", { status: request.status }),
        sseEvent("log", {
          id: 1,
          at: "2026-11-07T10:01:00Z",
          stream: "System",
          text: "$ terraform init -input=false -no-color",
        }),
        sseEvent("log", {
          id: 2,
          at: "2026-11-07T10:01:01Z",
          stream: "Stderr",
          text: "Warning: something",
        }),
        sseEvent("end", { status: request.status }),
      ]);
    }
    if (url.endsWith("/sensitive-outputs"))
      return jsonResponse({ root_password: "s3cret" });
    if (url.endsWith("/catalog")) return jsonResponse(catalog);
    return jsonResponse(request);
  });
  vi.stubGlobal("fetch", fetchMock);
}

function renderDetail(user = { id: "user-1", isAdmin: false }) {
  return render(
    <Providers>
      <RequestDetail requestId="req-1" user={user} />
    </Providers>,
  );
}

function calledUrls() {
  return fetchMock.mock.calls.map(([url]) => url as string);
}

describe("RequestDetail", () => {
  beforeEach(() => {
    replace.mockReset();
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("shows the request's status, settings, outputs and log", async () => {
    mockApi(testRequest("Succeeded"));
    renderDetail();

    expect(
      await screen.findByRole("heading", { name: /Droplet/ }),
    ).toHaveTextContent("Droplet web-1");
    expect(screen.getByText("Provisioned")).toBeInTheDocument();
    expect(screen.getByText("lon1")).toBeInTheDocument();
    expect(screen.getByText("192.0.2.1")).toBeInTheDocument();
    expect(await screen.findByText("Public IPv4 address.")).toBeInTheDocument();

    const log = screen.getByRole("log", { name: "Terraform log" });
    expect(
      await within(log).findByText("$ terraform init -input=false -no-color"),
    ).toHaveAttribute("data-stream", "System");
    expect(within(log).getByText("Warning: something")).toHaveAttribute(
      "data-stream",
      "Stderr",
    );
  });

  it("hides secret outputs until the owner reveals them, and hides them again", async () => {
    mockApi(testRequest("Succeeded"));
    const user = userEvent.setup();
    renderDetail();

    await user.click(
      await screen.findByRole("button", { name: "Reveal secrets" }),
    );
    expect(await screen.findByText("s3cret")).toBeInTheDocument();
    expect(calledUrls()).toContain(
      "/api/proxy/requests/req-1/sensitive-outputs",
    );

    await user.click(screen.getByRole("button", { name: "Hide secrets" }));
    expect(screen.queryByText("s3cret")).not.toBeInTheDocument();
  });

  it("never fetches secrets before the reveal", async () => {
    mockApi(testRequest("Succeeded"));
    renderDetail();

    await screen.findByRole("button", { name: "Reveal secrets" });
    expect(calledUrls()).not.toContain(
      "/api/proxy/requests/req-1/sensitive-outputs",
    );
  });

  it("offers no reveal to an admin looking at someone else's request, but lets them destroy it", async () => {
    mockApi(testRequest("Succeeded"));
    renderDetail({ id: "admin-1", isAdmin: true });

    expect(
      await screen.findByRole("button", { name: "Destroy" }),
    ).toBeInTheDocument();
    expect(screen.getByText(/Owner: user@example.com/)).toBeInTheDocument();
    expect(
      screen.queryByRole("button", { name: "Reveal secrets" }),
    ).not.toBeInTheDocument();
  });

  it("offers Destroy only once the request is provisioned or has failed", async () => {
    mockApi(testRequest("Applying"));
    renderDetail();

    await screen.findByText("Provisioning");
    expect(
      screen.queryByRole("button", { name: "Destroy" }),
    ).not.toBeInTheDocument();
  });

  it("doesn't let another user destroy", async () => {
    mockApi(testRequest("Failed"));
    renderDetail({ id: "someone-else", isAdmin: false });

    await screen.findByText("Failed");
    expect(
      screen.queryByRole("button", { name: "Destroy" }),
    ).not.toBeInTheDocument();
  });

  it("lists what it depends on and what depends on it", async () => {
    mockApi(
      testRequest("Succeeded", {
        dependents: [
          {
            id: "req-2",
            type: "firewall",
            displayName: "Cloud firewall",
            name: "web-fw",
            status: "Succeeded",
          },
        ],
      }),
    );
    renderDetail();

    expect(await screen.findByText("Needed by")).toBeInTheDocument();
    expect(
      screen.getAllByRole("link", { name: "Cloud firewall web-fw" })[0],
    ).toHaveAttribute("href", "/requests/req-2");
  });

  it("offers Destroy while something depends on it, since the destroy takes that too", async () => {
    mockApi(
      testRequest("Succeeded", {
        dependents: [
          {
            id: "req-2",
            type: "firewall",
            displayName: "Cloud firewall",
            name: "web-fw",
            status: "Succeeded",
          },
        ],
      }),
    );
    renderDetail();

    expect(
      await screen.findByRole("button", { name: "Destroy" }),
    ).toBeInTheDocument();
    expect(screen.queryByText(/first destroy/)).not.toBeInTheDocument();
  });

  it("shows a failed destroy and offers to retry it", async () => {
    mockApi(testRequest("DestroyFailed"));
    renderDetail();

    expect(await screen.findByText("Destroy failed")).toHaveAttribute(
      "data-status",
      "DestroyFailed",
    );
    expect(
      screen.getByRole("button", { name: "Retry destroy" }),
    ).toBeInTheDocument();
    expect(
      screen.getByText(/A retry destroys only this and what depends on it/),
    ).toBeInTheDocument();
  });

  it("says which request's failed destroy stopped this one's", async () => {
    mockApi(
      testRequest("Succeeded", {
        destroyStoppedBy: {
          id: "req-2",
          type: "firewall",
          displayName: "Cloud firewall",
          name: "web-fw",
          status: "DestroyFailed",
        },
      }),
    );
    renderDetail();

    expect(
      await screen.findByText(/Destroy stopped because/),
    ).toHaveTextContent(
      "Destroy stopped because Cloud firewall web-fw failed to destroy.",
    );
    expect(
      screen.getByRole("link", { name: "Cloud firewall web-fw" }),
    ).toHaveAttribute("href", "/requests/req-2");
  });

  it("offers Destroy when only a proposal depends on it", async () => {
    mockApi(
      testRequest("Succeeded", {
        dependents: [
          {
            id: "req-2",
            type: "firewall",
            displayName: "Cloud firewall",
            name: "web-fw",
            status: "AwaitingConfirmation",
          },
        ],
      }),
    );
    renderDetail();

    expect(
      await screen.findByRole("button", { name: "Destroy" }),
    ).toBeInTheDocument();
  });

  it("says what a queued request is waiting for", async () => {
    mockApi(
      testRequest("Queued", {
        dependsOn: [
          {
            id: "req-0",
            type: "droplet",
            displayName: "Droplet",
            name: "web-1",
            status: "Applying",
          },
        ],
      }),
    );
    renderDetail();

    expect(await screen.findByText(/Waiting for/)).toHaveTextContent(
      "Waiting for Droplet web-1 to be provisioned first.",
    );
  });

  it("explains a request that doesn't exist", async () => {
    vi.stubGlobal("fetch", async () =>
      jsonResponse({ detail: "Request req-1 doesn't exist." }, 404),
    );
    renderDetail();

    expect(await screen.findByRole("alert")).toHaveTextContent(
      "This request doesn't exist, or isn't yours.",
    );
    expect(replace).not.toHaveBeenCalled();
  });

  it("links to its project", async () => {
    mockApi(
      testRequest("Succeeded", { project: { id: "prj-1", name: "shop" } }),
    );
    renderDetail();

    expect(await screen.findByRole("link", { name: "shop" })).toHaveAttribute(
      "href",
      "/projects/prj-1",
    );
  });

  it.each([
    {
      when: "it was in a project",
      overrides: { project: { id: "prj-1", name: "shop" } },
      path: "/projects/prj-1",
      label: "its project",
      gone: "Droplet web-1",
    },
    {
      when: "it backed a project, which went with it",
      overrides: {
        type: "project",
        displayName: "Project",
        variables: { name: "shop" },
        project: { id: "prj-1", name: "shop" },
      },
      path: "/projects",
      label: "your projects",
      gone: "Project shop",
    },
    {
      when: "it had no project",
      overrides: {},
      path: "/requests",
      label: "your requests",
      gone: "Droplet web-1",
    },
  ])(
    "goes to $path once a destroy deletes the request when $when",
    async ({ overrides, path, label, gone }) => {
      let deleted = false;
      vi.stubGlobal(
        "fetch",
        vi.fn(async (url: string) => {
          if (url.endsWith("/catalog")) return jsonResponse(catalog);
          if (url.endsWith("/logs")) {
            // The runner finishes the destroy and deletes the request while the log is open.
            deleted = true;
            return sseResponse([
              sseEvent("status", { status: "Destroying" }),
              sseEvent("deleted", {}),
            ]);
          }
          return deleted
            ? jsonResponse({ detail: "Request req-1 doesn't exist." }, 404)
            : jsonResponse(testRequest("Destroying", overrides));
        }),
      );
      renderDetail();

      await waitFor(() => expect(replace).toHaveBeenCalledWith(path));
      expect(
        await screen.findByText(
          `${gone} was destroyed. Taking you back to ${label}…`,
        ),
      ).toBeInTheDocument();
      expect(screen.queryByRole("alert")).not.toBeInTheDocument();
    },
  );
});
