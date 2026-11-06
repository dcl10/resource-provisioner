import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { UsersAdmin } from "@/components/admin/users-admin";
import { Providers } from "@/components/providers";
import type { Invitation } from "@/lib/invitations";

const pending: Invitation = {
  id: "inv-1",
  email: "pending@example.com",
  status: "Pending",
  invitedBy: "admin-1",
  sentAt: "2026-11-01T10:00:00Z",
  expiresAt: "2026-11-08T10:00:00Z",
  revokedAt: null,
};
const accepted: Invitation = {
  ...pending,
  id: "inv-2",
  email: "accepted@example.com",
  status: "Accepted",
};

let fetchMock: ReturnType<typeof vi.fn>;

function json(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": "application/json" },
  });
}

/** Routes the component's API calls; `post` answers the POSTs. */
function mockApi(post: (url: string, init: RequestInit) => Response) {
  fetchMock = vi.fn(async (url: string, init?: RequestInit) =>
    init?.method === "POST" ? post(url, init) : json([pending, accepted]),
  );
  vi.stubGlobal("fetch", fetchMock);
}

function renderPage() {
  return render(
    <Providers>
      <UsersAdmin />
    </Providers>,
  );
}

function postsTo(url: string) {
  return fetchMock.mock.calls.filter(
    ([calledUrl, init]) => calledUrl === url && init?.method === "POST",
  );
}

describe("UsersAdmin", () => {
  beforeEach(() => {
    mockApi(() => json(pending));
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("lists invitations with their status", async () => {
    renderPage();

    const row = (await screen.findByText("pending@example.com")).closest("tr")!;
    expect(within(row).getByText("Pending")).toBeInTheDocument();
    const acceptedRow = screen.getByText("accepted@example.com").closest("tr")!;
    expect(within(acceptedRow).getByText("Accepted")).toBeInTheDocument();
  });

  it("offers resend and revoke only for open invitations", async () => {
    renderPage();

    const row = (await screen.findByText("pending@example.com")).closest("tr")!;
    expect(within(row).getByRole("button", { name: "Resend" })).toBeVisible();
    const acceptedRow = screen.getByText("accepted@example.com").closest("tr")!;
    expect(within(acceptedRow).queryByRole("button")).not.toBeInTheDocument();
  });

  it("shows a validation error and doesn't call the API for a bad email", async () => {
    const user = userEvent.setup();
    renderPage();

    await user.type(screen.getByLabelText("Email address"), "nope");
    await user.click(screen.getByRole("button", { name: "Send invite" }));

    expect(
      await screen.findByText("Enter a valid email address."),
    ).toBeInTheDocument();
    expect(postsTo("/api/proxy/invitations")).toHaveLength(0);
  });

  it("invites a colleague and confirms with a toast", async () => {
    mockApi(() => json({ ...pending, email: "new@example.com" }, 201));
    const user = userEvent.setup();
    renderPage();

    await user.type(screen.getByLabelText("Email address"), "new@example.com");
    await user.click(screen.getByRole("button", { name: "Send invite" }));

    expect(
      await screen.findByText("Invitation sent to new@example.com"),
    ).toBeInTheDocument();
    expect(postsTo("/api/proxy/invitations")).toHaveLength(1);
    expect(screen.getByLabelText("Email address")).toHaveValue("");
  });

  it("shows the API's message in a toast when the invite fails", async () => {
    mockApi(() =>
      json({ detail: "taken@example.com already has an account." }, 409),
    );
    const user = userEvent.setup();
    renderPage();

    await user.type(
      screen.getByLabelText("Email address"),
      "taken@example.com",
    );
    await user.click(screen.getByRole("button", { name: "Send invite" }));

    expect(
      await screen.findByText("taken@example.com already has an account."),
    ).toBeInTheDocument();
  });

  it("resends an invitation", async () => {
    const user = userEvent.setup();
    renderPage();

    await user.click(await screen.findByRole("button", { name: "Resend" }));

    expect(
      await screen.findByText("Invitation resent to pending@example.com"),
    ).toBeInTheDocument();
    expect(postsTo("/api/proxy/invitations/inv-1/resend")).toHaveLength(1);
  });

  it("revokes an invitation after confirmation", async () => {
    mockApi(() => json({ ...pending, status: "Revoked" }));
    const user = userEvent.setup();
    renderPage();

    await user.click(await screen.findByRole("button", { name: "Revoke" }));
    expect(postsTo("/api/proxy/invitations/inv-1/revoke")).toHaveLength(0);

    const dialog = await screen.findByRole("alertdialog");
    await user.click(within(dialog).getByRole("button", { name: "Revoke" }));

    await waitFor(() =>
      expect(postsTo("/api/proxy/invitations/inv-1/revoke")).toHaveLength(1),
    );
    expect(
      await screen.findByText("Invitation to pending@example.com revoked"),
    ).toBeInTheDocument();
  });
});
