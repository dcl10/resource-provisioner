import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { ProposalCard } from "@/components/chat/proposal-card";
import { Providers } from "@/components/providers";
import type { Proposal } from "@/lib/chat";
import type { ProvisioningRequest, RequestStatus } from "@/lib/requests";

const push = vi.fn();
vi.mock("next/navigation", () => ({ useRouter: () => ({ push }) }));

const proposal: Proposal = {
  requestId: "req-1",
  type: "droplet",
  displayName: "Droplet",
  costHint: "About $4 to $6 a month.",
  items: [
    { name: "name", label: "Name", value: "web-1" },
    { name: "region", label: "Region", value: "lon1" },
  ],
};

function request(status: RequestStatus): ProvisioningRequest {
  return {
    id: "req-1",
    ownerId: "user-1",
    ownerEmail: "user@example.com",
    type: "droplet",
    displayName: "Droplet",
    status,
    variables: { name: "web-1", region: "lon1" },
    outputs: null,
    conversationId: "conv-1",
    createdAt: "2026-11-07T10:00:00Z",
    updatedAt: "2026-11-07T10:00:00Z",
    confirmedAt: null,
    dependsOn: [],
    dependents: [],
  };
}

function json(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": "application/json" },
  });
}

let fetchMock: ReturnType<typeof vi.fn>;

const notFound = () =>
  json({ title: "Not Found", detail: "Request req-1 doesn't exist." }, 404);

/**
 * Answers GET /requests/req-1 with the request in `initial` status (404 if null), and the POSTs with `post`. Like the
 * API, a successful POST changes what GET returns, and a 204 (a cancel) deletes the request.
 */
function mockApi(
  initial: RequestStatus | null,
  post: (url: string) => Response = () => json(request("Queued")),
) {
  let current: ProvisioningRequest | null = initial ? request(initial) : null;
  fetchMock = vi.fn(async (url: string, init?: RequestInit) => {
    if (init?.method !== "POST") return current ? json(current) : notFound();
    const response = post(url);
    if (response.status === 204) current = null;
    else if (response.ok)
      current = (await response.clone().json()) as ProvisioningRequest;
    return response;
  });
  vi.stubGlobal("fetch", fetchMock);
}

function posts() {
  return fetchMock.mock.calls
    .filter(([, init]) => init?.method === "POST")
    .map(([url]) => url as string);
}

/** Finds a button once the request's status has loaded and it's enabled. */
async function enabledButton(name: string) {
  const button = await screen.findByRole("button", { name });
  await waitFor(() => expect(button).toBeEnabled());
  return button;
}

function renderCard() {
  return render(
    <Providers>
      <ProposalCard proposal={proposal} />
    </Providers>,
  );
}

describe("ProposalCard", () => {
  beforeEach(() => {
    push.mockReset();
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("disables the buttons until the request's status has loaded", () => {
    mockApi("AwaitingConfirmation");
    renderCard();

    expect(screen.getByRole("button", { name: "Confirm" })).toBeDisabled();
    expect(screen.getByRole("button", { name: "Cancel" })).toBeDisabled();
  });

  it("shows the resource type, its variables and the cost hint", async () => {
    mockApi("AwaitingConfirmation");
    renderCard();

    const card = screen.getByRole("generic", { name: "Proposed Droplet" });
    expect(within(card).getByText("Droplet")).toBeInTheDocument();
    expect(within(card).getByText("droplet")).toBeInTheDocument();
    const nameRow = within(card).getByText("Name").closest("tr")!;
    expect(within(nameRow).getByText("web-1")).toBeInTheDocument();
    const regionRow = within(card).getByText("Region").closest("tr")!;
    expect(within(regionRow).getByText("lon1")).toBeInTheDocument();
    expect(
      within(card).getByText("About $4 to $6 a month."),
    ).toBeInTheDocument();
    expect(
      await within(card).findByText("Awaiting confirmation"),
    ).toBeInTheDocument();
  });

  it("says what it runs after, with links", async () => {
    mockApi("AwaitingConfirmation");
    render(
      <Providers>
        <ProposalCard
          proposal={{
            ...proposal,
            dependsOn: [
              {
                id: "req-0",
                type: "droplet",
                displayName: "Droplet",
                name: "web-1",
                status: "AwaitingConfirmation",
              },
            ],
          }}
        />
      </Providers>,
    );

    expect(screen.getByText("Runs after:")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "Droplet web-1" })).toHaveAttribute(
      "href",
      "/requests/req-0",
    );
  });

  it("names the project it goes in", () => {
    mockApi("AwaitingConfirmation");
    render(
      <Providers>
        <ProposalCard
          proposal={{ ...proposal, project: { id: "prj-1", name: "shop" } }}
        />
      </Providers>,
    );

    const line = screen.getByText("Project:").closest("p")!;
    expect(line).toHaveTextContent("Project: shop");
  });

  it("doesn't name a project request's own project again", () => {
    mockApi("AwaitingConfirmation");
    render(
      <Providers>
        <ProposalCard
          proposal={{
            ...proposal,
            type: "project",
            displayName: "Project",
            items: [{ name: "name", label: "Name", value: "shop" }],
            project: { id: "prj-1", name: "shop" },
          }}
        />
      </Providers>,
    );

    expect(screen.queryByText("Project:")).not.toBeInTheDocument();
  });

  it("confirms the request, then shows a toast and a link to it", async () => {
    mockApi("AwaitingConfirmation");
    const user = userEvent.setup();
    renderCard();

    await user.click(await enabledButton("Confirm"));

    expect(await screen.findByText("Droplet confirmed")).toBeInTheDocument();
    expect(posts()).toEqual(["/api/proxy/requests/req-1/confirm"]);
    expect(screen.getByText("Queued")).toBeInTheDocument();
    expect(
      screen.queryByRole("button", { name: "Confirm" }),
    ).not.toBeInTheDocument();
    const link = screen.getByRole("link", { name: "View request" });
    expect(link).toHaveAttribute("href", "/requests/req-1");

    await user.click(screen.getByRole("button", { name: "View request" }));
    expect(push).toHaveBeenCalledWith("/requests/req-1");
  });

  it("cancels the request, after which it no longer exists", async () => {
    mockApi("AwaitingConfirmation", () => new Response(null, { status: 204 }));
    const user = userEvent.setup();
    renderCard();

    await user.click(await enabledButton("Cancel"));

    expect(
      await screen.findByText("Droplet proposal cancelled"),
    ).toBeInTheDocument();
    expect(posts()).toEqual(["/api/proxy/requests/req-1/cancel"]);
    expect(await screen.findByText("No longer exists")).toBeInTheDocument();
    expect(
      screen.queryByRole("button", { name: "Confirm" }),
    ).not.toBeInTheDocument();
    expect(
      screen.queryByRole("link", { name: "View request" }),
    ).not.toBeInTheDocument();
  });

  it("says a request that's been cancelled or destroyed since no longer exists", async () => {
    mockApi(null);
    renderCard();

    expect(await screen.findByText("No longer exists")).toBeInTheDocument();
    expect(
      screen.getByText(/This request no longer exists/),
    ).toBeInTheDocument();
    expect(
      screen.queryByRole("button", { name: "Confirm" }),
    ).not.toBeInTheDocument();
    expect(
      screen.queryByRole("button", { name: "Cancel" }),
    ).not.toBeInTheDocument();
    expect(
      screen.queryByText(/Couldn't load this request's status/),
    ).not.toBeInTheDocument();
  });

  it("explains a spend limit and keeps the buttons", async () => {
    const detail =
      "You already have 3 active requests, and each user can have at most 3. Destroy one before confirming another.";
    mockApi("AwaitingConfirmation", () =>
      json({ title: "Active request limit reached", detail, status: 409 }, 409),
    );
    const user = userEvent.setup();
    renderCard();

    await user.click(await enabledButton("Confirm"));

    const alert = await screen.findByRole("alert");
    expect(within(alert).getByText("You're at your limit")).toBeInTheDocument();
    expect(within(alert).getByText(detail)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Confirm" })).toBeEnabled();
  });

  it("shows the API's message when the variables are no longer valid", async () => {
    mockApi("AwaitingConfirmation", () =>
      json(
        {
          title: "The request's variables are no longer valid.",
          errors: {
            "droplet_request_ids[0]": ["Droplet request x is Destroying."],
          },
          status: 422,
        },
        422,
      ),
    );
    const user = userEvent.setup();
    renderCard();

    await user.click(await enabledButton("Confirm"));

    const alert = await screen.findByRole("alert");
    expect(
      within(alert).getByText("Droplet request x is Destroying."),
    ).toBeInTheDocument();
  });

  it("shows the status and a link instead of buttons once confirmed", async () => {
    mockApi("Succeeded");
    renderCard();

    expect(await screen.findByText("Provisioned")).toBeInTheDocument();
    expect(
      screen.queryByRole("button", { name: "Confirm" }),
    ).not.toBeInTheDocument();
    expect(screen.getByRole("link", { name: "View request" })).toHaveAttribute(
      "href",
      "/requests/req-1",
    );
  });
});
