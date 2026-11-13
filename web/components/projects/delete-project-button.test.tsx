import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";

import { DeleteProjectButton } from "@/components/projects/delete-project-button";
import { Providers } from "@/components/providers";
import type { DestroyPlan, RequestLink } from "@/lib/requests";
import { testProject } from "@/lib/test-projects";
import { jsonResponse } from "@/lib/test-requests";

const firewall: RequestLink = {
  id: "req-fw",
  type: "firewall",
  displayName: "Cloud firewall",
  name: "web-fw",
  status: "Succeeded",
};
const droplet: RequestLink = {
  id: "req-web",
  type: "droplet",
  displayName: "Droplet",
  name: "web-1",
  status: "Succeeded",
};
const projectRequest: RequestLink = {
  id: "req-p",
  type: "project",
  displayName: "Project",
  name: "shop",
  status: "Succeeded",
};

const plan: DestroyPlan = {
  canDestroy: true,
  reason: null,
  destroy: [firewall, droplet, projectRequest],
  cancel: [],
  waitFor: [],
};

let fetchMock: ReturnType<typeof vi.fn>;

function mockApi(destroyPlan: DestroyPlan, remove: () => Response) {
  fetchMock = vi.fn(async (url: string) =>
    url.endsWith("/destroy-plan") ? jsonResponse(destroyPlan) : remove(),
  );
  vi.stubGlobal("fetch", fetchMock);
}

function deleteCalls() {
  return fetchMock.mock.calls.filter(
    ([, init]) => (init as RequestInit | undefined)?.method === "DELETE",
  );
}

function renderButton(status: "Succeeded" | "DestroyFailed" = "Succeeded") {
  return render(
    <Providers>
      <DeleteProjectButton project={testProject(status)} />
    </Providers>,
  );
}

describe("DeleteProjectButton", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("lists everything in the project that's destroyed, the project last, from its request's plan", async () => {
    mockApi(plan, () => jsonResponse(testProject("Destroying"), 202));
    const user = userEvent.setup();
    renderButton();

    await user.click(screen.getByRole("button", { name: "Delete project" }));
    const dialog = await screen.findByRole("alertdialog");

    expect(
      within(dialog).getByText("Delete project shop?"),
    ).toBeInTheDocument();
    const destroyed = await within(dialog).findByRole("list", {
      name: "Destroyed, in this order",
    });
    expect(
      within(destroyed)
        .getAllByRole("listitem")
        .map((item) => item.textContent),
    ).toEqual([
      "Cloud firewall web-fwProvisioned",
      "Droplet web-1Provisioned",
      "Project shopProvisioned",
    ]);
    expect(fetchMock).toHaveBeenCalledWith(
      "/api/proxy/requests/req-p/destroy-plan",
      expect.anything(),
    );
    expect(deleteCalls()).toHaveLength(0);
  });

  it("deletes the project after confirmation and says so", async () => {
    mockApi(plan, () => jsonResponse(testProject("Destroying"), 202));
    const user = userEvent.setup();
    renderButton();

    await user.click(screen.getByRole("button", { name: "Delete project" }));
    const dialog = await screen.findByRole("alertdialog");
    await within(dialog).findByRole("list", {
      name: "Destroyed, in this order",
    });
    await user.click(
      within(dialog).getByRole("button", { name: "Delete project" }),
    );

    expect(
      await screen.findByText("Deleting project shop"),
    ).toBeInTheDocument();
    expect(fetchMock).toHaveBeenCalledWith(
      "/api/proxy/projects/prj-1",
      expect.objectContaining({ method: "DELETE" }),
    );
    await waitFor(() =>
      expect(screen.queryByRole("alertdialog")).not.toBeInTheDocument(),
    );
  });

  it("explains why it can't be deleted yet, and doesn't offer to", async () => {
    mockApi(
      {
        canDestroy: false,
        reason:
          "Wait for Droplet web-1 to finish, then try again: it depends on this request.",
        destroy: [projectRequest],
        cancel: [],
        waitFor: [{ ...droplet, status: "Queued" }],
      },
      () => jsonResponse(testProject("Destroying"), 202),
    );
    const user = userEvent.setup();
    renderButton();

    await user.click(screen.getByRole("button", { name: "Delete project" }));
    const dialog = await screen.findByRole("alertdialog");

    expect(await within(dialog).findByRole("alert")).toHaveTextContent(
      "Wait for Droplet web-1 to finish",
    );
    expect(
      within(dialog).getByRole("button", { name: "Delete project" }),
    ).toBeDisabled();
  });

  it("shows the API's message when the delete is refused", async () => {
    mockApi(plan, () =>
      jsonResponse(
        {
          detail:
            "This request is AwaitingConfirmation, so it can't be destroyed.",
        },
        409,
      ),
    );
    const user = userEvent.setup();
    renderButton();

    await user.click(screen.getByRole("button", { name: "Delete project" }));
    const dialog = await screen.findByRole("alertdialog");
    await within(dialog).findByRole("list", {
      name: "Destroyed, in this order",
    });
    await user.click(
      within(dialog).getByRole("button", { name: "Delete project" }),
    );

    expect(
      await screen.findByText(
        "This request is AwaitingConfirmation, so it can't be destroyed.",
      ),
    ).toBeInTheDocument();
  });

  it("offers a retry once the project's own destroy has failed", async () => {
    mockApi(
      { ...plan, destroy: [{ ...projectRequest, status: "DestroyFailed" }] },
      () => jsonResponse(testProject("Destroying"), 202),
    );
    const user = userEvent.setup();
    renderButton("DestroyFailed");

    await user.click(screen.getByRole("button", { name: "Retry delete" }));
    const dialog = await screen.findByRole("alertdialog");

    expect(
      within(dialog).getByText(/The project's own destroy failed/),
    ).toBeInTheDocument();
    expect(
      await within(dialog).findByRole("list", { name: "Destroyed" }),
    ).toBeInTheDocument();
  });
});
