import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";

import { ChatThread } from "@/components/chat/chat-thread";
import { Providers } from "@/components/providers";
import type { ChatMessage, Proposal } from "@/lib/chat";
import { sseEvent, sseResponse } from "@/lib/test-sse";

vi.mock("next/navigation", () => ({ useRouter: () => ({ push: vi.fn() }) }));

const proposal: Proposal = {
  requestId: "req-1",
  type: "droplet",
  displayName: "Droplet",
  costHint: "About $4 a month.",
  items: [{ name: "name", label: "Name", value: "web-1" }],
};

function json(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": "application/json" },
  });
}

/** Answers the message POST with `reply`, and the proposal card's status GET. */
function mockApi(reply: () => Response) {
  const fetchMock = vi.fn(async (url: string) =>
    url.endsWith("/messages")
      ? reply()
      : json({ id: "req-1", status: "AwaitingConfirmation" }),
  );
  vi.stubGlobal("fetch", fetchMock);
  return fetchMock;
}

function renderThread(initialMessages: ChatMessage[] = []) {
  return render(
    <Providers>
      <ChatThread conversationId="conv-1" initialMessages={initialMessages} />
    </Providers>,
  );
}

describe("ChatThread", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("shows the earlier messages, rendering the assistant's Markdown", () => {
    mockApi(() => sseResponse([]));
    renderThread([
      { id: "1", role: "user", text: "a server", proposals: [] },
      {
        id: "2",
        role: "assistant",
        text: "Which **region**?\n\n- lon1\n- nyc3",
        proposals: [],
      },
    ]);

    expect(screen.getByText("a server")).toBeInTheDocument();
    expect(screen.getByText("region").tagName).toBe("STRONG");
    expect(
      within(screen.getByRole("list"))
        .getAllByRole("listitem")
        .map((item) => item.textContent),
    ).toEqual(["lon1", "nyc3"]);
  });

  it("never renders raw HTML from the assistant", () => {
    mockApi(() => sseResponse([]));
    renderThread([
      {
        id: "1",
        role: "assistant",
        text: 'Hi <img src="x" onerror="alert(1)"> <script>alert(2)</script>',
        proposals: [],
      },
    ]);

    const message = screen.getByText(/Hi/).closest("div")!;
    expect(message.querySelector("img")).toBeNull();
    expect(message.querySelector("script")).toBeNull();
    expect(message.querySelector("[onerror]")).toBeNull();
  });

  it("sends the message and streams the reply, with a card for each proposal", async () => {
    const fetchMock = mockApi(() =>
      sseResponse([
        sseEvent("text", { text: "Here's the " }),
        sseEvent("text", { text: "proposal." }),
        sseEvent("proposal", proposal),
        sseEvent("done", {}),
      ]),
    );
    const user = userEvent.setup();
    renderThread();

    await user.type(screen.getByLabelText("Message"), "a droplet{Enter}");

    expect(await screen.findByText("Here's the proposal.")).toBeInTheDocument();
    expect(
      await screen.findByRole("generic", { name: "Proposed Droplet" }),
    ).toBeInTheDocument();
    expect(screen.getByText("a droplet")).toBeInTheDocument();
    expect(screen.getByLabelText("Message")).toHaveValue("");
    expect(fetchMock).toHaveBeenCalledWith(
      "/api/proxy/conversations/conv-1/messages",
      expect.objectContaining({ body: '{"text":"a droplet"}' }),
    );
  });

  it("puts a rate-limited message back in the box with a friendly explanation", async () => {
    mockApi(() => json({ title: "Too many messages", status: 429 }, 429));
    const user = userEvent.setup();
    renderThread();

    await user.type(screen.getByLabelText("Message"), "hello{Enter}");

    expect(await screen.findByRole("alert")).toHaveTextContent(
      /faster than the app allows/,
    );
    expect(screen.getByLabelText("Message")).toHaveValue("hello");
    expect(screen.queryByText("hello", { selector: "div" })).toBeNull();
  });

  it("keeps the partial reply and shows the error when the reply stops early", async () => {
    mockApi(() =>
      sseResponse([
        sseEvent("text", { text: "Partial answer" }),
        sseEvent("error", { error: "Claude declined to answer that." }),
      ]),
    );
    const user = userEvent.setup();
    renderThread();

    await user.type(screen.getByLabelText("Message"), "hello{Enter}");

    expect(await screen.findByRole("alert")).toHaveTextContent(
      "Claude declined to answer that.",
    );
    expect(screen.getByText("Partial answer")).toBeInTheDocument();
    expect(screen.getByText("hello")).toBeInTheDocument();
  });

  it("doesn't send an empty message, and Shift+Enter adds a line", async () => {
    const fetchMock = mockApi(() => sseResponse([]));
    const user = userEvent.setup();
    renderThread();

    await user.type(screen.getByLabelText("Message"), "   {Enter}");
    await user.type(
      screen.getByLabelText("Message"),
      "a{Shift>}{Enter}{/Shift}b",
    );

    expect(screen.getByLabelText("Message")).toHaveValue("   a\nb");
    await waitFor(() => expect(fetchMock).not.toHaveBeenCalled());
  });
});
