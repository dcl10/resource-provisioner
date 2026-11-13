import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";

import { Providers } from "@/components/providers";
import { DestroyButton } from "@/components/requests/destroy-button";
import type {
  DestroyPlan,
  ProvisioningRequest,
  RequestLink,
} from "@/lib/requests";
import { jsonResponse, testRequest } from "@/lib/test-requests";

const firewall: RequestLink = {
  id: "req-2",
  type: "firewall",
  displayName: "Cloud firewall",
  name: "web-fw",
  status: "Succeeded",
};
const proposal: RequestLink = {
  id: "req-3",
  type: "volume",
  displayName: "Volume",
  name: "data",
  status: "AwaitingConfirmation",
};
const self: RequestLink = {
  id: "req-1",
  type: "droplet",
  displayName: "Droplet",
  name: "web-1",
  status: "Succeeded",
};

const cascade: DestroyPlan = {
  canDestroy: true,
  reason: null,
  destroy: [firewall, self],
  cancel: [proposal],
  waitFor: [],
};

let fetchMock: ReturnType<typeof vi.fn>;

function mockApi(plan: DestroyPlan, destroy: () => Response) {
  fetchMock = vi.fn(async (url: string) =>
    url.endsWith("/destroy-plan") ? jsonResponse(plan) : destroy(),
  );
  vi.stubGlobal("fetch", fetchMock);
}

function destroyCalls() {
  return fetchMock.mock.calls.filter(([url]) =>
    (url as string).endsWith("/destroy"),
  );
}

function renderButton(request: ProvisioningRequest = testRequest("Succeeded")) {
  return render(
    <Providers>
      <DestroyButton request={request} />
    </Providers>,
  );
}

describe("DestroyButton", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("lists what's destroyed, in order, and what's cancelled before destroying anything", async () => {
    mockApi(cascade, () => jsonResponse(testRequest("Destroying")));
    const user = userEvent.setup();
    renderButton();

    await user.click(screen.getByRole("button", { name: "Destroy" }));

    const dialog = await screen.findByRole("alertdialog");
    expect(
      within(dialog).getByText('Destroy Droplet "web-1"?'),
    ).toBeInTheDocument();
    const destroyed = await within(dialog).findByRole("list", {
      name: "Destroyed, in this order",
    });
    expect(
      within(destroyed)
        .getAllByRole("listitem")
        .map((item) => item.textContent),
    ).toEqual(["Cloud firewall web-fwProvisioned", "Droplet web-1Provisioned"]);
    const cancelled = within(dialog).getByRole("list", {
      name: "Proposals cancelled",
    });
    expect(within(cancelled).getByText("Volume data")).toBeInTheDocument();
    expect(within(dialog).getByText(/can't be undone/)).toBeInTheDocument();
    expect(fetchMock).toHaveBeenCalledWith(
      "/api/proxy/requests/req-1/destroy-plan",
      expect.anything(),
    );
    expect(destroyCalls()).toHaveLength(0);
  });

  it("does nothing when the user keeps it", async () => {
    mockApi(cascade, () => jsonResponse(testRequest("Destroying")));
    const user = userEvent.setup();
    renderButton();

    await user.click(screen.getByRole("button", { name: "Destroy" }));
    await user.click(await screen.findByRole("button", { name: "Keep it" }));

    await waitFor(() =>
      expect(screen.queryByRole("alertdialog")).not.toBeInTheDocument(),
    );
    expect(destroyCalls()).toHaveLength(0);
  });

  it("destroys after confirmation, says so and closes", async () => {
    mockApi(cascade, () => jsonResponse(testRequest("Destroying")));
    const user = userEvent.setup();
    renderButton();

    await user.click(screen.getByRole("button", { name: "Destroy" }));
    const dialog = await screen.findByRole("alertdialog");
    await within(dialog).findByRole("list", {
      name: "Destroyed, in this order",
    });
    await user.click(within(dialog).getByRole("button", { name: "Destroy" }));

    expect(
      await screen.findByText('Destroying Droplet "web-1"'),
    ).toBeInTheDocument();
    expect(fetchMock).toHaveBeenCalledWith(
      "/api/proxy/requests/req-1/destroy",
      expect.objectContaining({ method: "POST" }),
    );
    await waitFor(() =>
      expect(screen.queryByRole("alertdialog")).not.toBeInTheDocument(),
    );
  });

  it("explains why it can't be destroyed yet, and doesn't offer to", async () => {
    mockApi(
      {
        canDestroy: false,
        reason:
          "Wait for Cloud firewall web-fw to finish, then try again: it depends on this request.",
        destroy: [self],
        cancel: [],
        waitFor: [{ ...firewall, status: "Applying" }],
      },
      () => jsonResponse(testRequest("Destroying")),
    );
    const user = userEvent.setup();
    renderButton();

    await user.click(screen.getByRole("button", { name: "Destroy" }));
    const dialog = await screen.findByRole("alertdialog");

    expect(await within(dialog).findByRole("alert")).toHaveTextContent(
      "Wait for Cloud firewall web-fw to finish",
    );
    const running = within(dialog).getByRole("list", { name: "Still running" });
    expect(within(running).getByText("Provisioning")).toBeInTheDocument();
    expect(
      within(dialog).getByRole("button", { name: "Destroy" }),
    ).toBeDisabled();
  });

  it("shows the API's message when destroy is refused", async () => {
    mockApi(cascade, () =>
      jsonResponse(
        { detail: "This request is Destroying, so it can't be destroyed." },
        409,
      ),
    );
    const user = userEvent.setup();
    renderButton();

    await user.click(screen.getByRole("button", { name: "Destroy" }));
    const dialog = await screen.findByRole("alertdialog");
    await within(dialog).findByRole("list", {
      name: "Destroyed, in this order",
    });
    await user.click(within(dialog).getByRole("button", { name: "Destroy" }));

    expect(
      await screen.findByText(
        "This request is Destroying, so it can't be destroyed.",
      ),
    ).toBeInTheDocument();
  });

  it("offers a retry after a failed destroy, saying what it destroys", async () => {
    mockApi(
      {
        ...cascade,
        destroy: [{ ...self, status: "DestroyFailed" }],
        cancel: [],
      },
      () => jsonResponse(testRequest("Destroying")),
    );
    const user = userEvent.setup();
    renderButton(testRequest("DestroyFailed"));

    await user.click(screen.getByRole("button", { name: "Retry destroy" }));
    const dialog = await screen.findByRole("alertdialog");

    expect(
      within(dialog).getByText('Retry destroying Droplet "web-1"?'),
    ).toBeInTheDocument();
    expect(
      within(dialog).getByText(/destroys only this and what depends on it/),
    ).toBeInTheDocument();
    const destroyed = await within(dialog).findByRole("list", {
      name: "Destroyed",
    });
    expect(within(destroyed).getByText("Destroy failed")).toBeInTheDocument();

    await user.click(
      within(dialog).getByRole("button", { name: "Retry destroy" }),
    );
    await waitFor(() => expect(destroyCalls()).toHaveLength(1));
  });
});
