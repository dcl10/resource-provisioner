import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";

import { ConfirmAllButton } from "@/components/chat/confirm-all-button";
import { Providers } from "@/components/providers";
import type { Proposal } from "@/lib/chat";
import type { RequestStatus } from "@/lib/requests";
import { jsonResponse, testRequest } from "@/lib/test-requests";

const push = vi.fn();
vi.mock("next/navigation", () => ({ useRouter: () => ({ push }) }));

function proposal(id: string, displayName: string): Proposal {
  return {
    requestId: id,
    type: "droplet",
    displayName,
    costHint: "$4",
    items: [],
  };
}

const droplet = proposal("req-1", "Droplet");
const firewall = proposal("req-2", "Cloud firewall");

let fetchMock: ReturnType<typeof vi.fn>;

/** Answers each request's GET with its status, and the confirm-all POST with `post`. */
function mockApi(
  statuses: Record<string, RequestStatus>,
  post: () => Response = () =>
    jsonResponse([
      testRequest("Queued", { id: "req-1" }),
      testRequest("Queued", { id: "req-2" }),
    ]),
) {
  fetchMock = vi.fn(async (url: string, init?: RequestInit) => {
    if (init?.method === "POST") return post();
    const id = url.split("/").at(-1)!;
    return jsonResponse(testRequest(statuses[id], { id }));
  });
  vi.stubGlobal("fetch", fetchMock);
}

function renderButton(proposals = [droplet, firewall]) {
  return render(
    <Providers>
      <ConfirmAllButton proposals={proposals} />
    </Providers>,
  );
}

describe("ConfirmAllButton", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("offers to confirm every proposal still awaiting confirmation", async () => {
    mockApi({
      "req-1": "AwaitingConfirmation",
      "req-2": "AwaitingConfirmation",
    });
    renderButton();

    expect(
      await screen.findByRole("button", { name: "Confirm all (2)" }),
    ).toBeInTheDocument();
  });

  it("isn't shown when fewer than two are waiting", async () => {
    mockApi({ "req-1": "Queued", "req-2": "AwaitingConfirmation" });
    renderButton();

    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(2));
    expect(screen.queryByRole("button")).not.toBeInTheDocument();
  });

  it("confirms them together and says so", async () => {
    mockApi({
      "req-1": "AwaitingConfirmation",
      "req-2": "AwaitingConfirmation",
    });
    const user = userEvent.setup();
    renderButton();

    await user.click(
      await screen.findByRole("button", { name: "Confirm all (2)" }),
    );

    expect(await screen.findByText("2 requests confirmed")).toBeInTheDocument();
    const post = fetchMock.mock.calls.find(
      ([, init]) => init?.method === "POST",
    )!;
    expect(post[0]).toBe("/api/proxy/requests/confirm");
    expect(JSON.parse(post[1].body as string)).toEqual({
      requestIds: ["req-1", "req-2"],
    });
    await waitFor(() =>
      expect(
        screen.queryByRole("button", { name: /Confirm all/ }),
      ).not.toBeInTheDocument(),
    );
  });

  it("explains a spend limit, and confirms nothing", async () => {
    const detail =
      "You have 2 active requests; confirming these 2 would make 4, over the limit of 3 per user. Destroy something first, or confirm fewer.";
    mockApi(
      { "req-1": "AwaitingConfirmation", "req-2": "AwaitingConfirmation" },
      () =>
        jsonResponse(
          { title: "Active request limit reached", detail, status: 409 },
          409,
        ),
    );
    const user = userEvent.setup();
    renderButton();

    await user.click(
      await screen.findByRole("button", { name: "Confirm all (2)" }),
    );

    expect(await screen.findByRole("alert")).toHaveTextContent(detail);
    expect(screen.getByText("You're at your limit")).toBeInTheDocument();
    expect(
      screen.getByRole("button", { name: "Confirm all (2)" }),
    ).toBeEnabled();
  });
});
