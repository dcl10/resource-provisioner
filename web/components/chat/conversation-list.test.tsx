import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { ConversationList } from "@/components/chat/conversation-list";
import { Providers } from "@/components/providers";
import type { ConversationSummary } from "@/lib/chat";

const push = vi.fn();
let params: { id?: string } = {};
vi.mock("next/navigation", () => ({
  useRouter: () => ({ push }),
  useParams: () => params,
}));

function conversation(id: string, title: string | null): ConversationSummary {
  return {
    id,
    ownerId: "user-1",
    ownerEmail: null,
    title,
    createdAt: "2026-11-07T10:00:00Z",
    updatedAt: "2026-11-07T10:00:00Z",
  };
}

function json(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": "application/json" },
  });
}

let fetchMock: ReturnType<typeof vi.fn>;

function renderList() {
  return render(
    <Providers>
      <ConversationList />
    </Providers>,
  );
}

describe("ConversationList", () => {
  beforeEach(() => {
    push.mockReset();
    params = {};
    fetchMock = vi.fn(async (_url: string, init?: RequestInit) =>
      init?.method === "POST"
        ? json(conversation("conv-new", null), 201)
        : json([
            conversation("conv-2", "A Postgres database"),
            conversation("conv-1", null),
          ]),
    );
    vi.stubGlobal("fetch", fetchMock);
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("links to each conversation and highlights the open one", async () => {
    params = { id: "conv-2" };
    renderList();

    const open = await screen.findByRole("link", {
      name: "A Postgres database",
    });
    expect(open).toHaveAttribute("href", "/chat/conv-2");
    expect(open).toHaveAttribute("aria-current", "page");
    const untitled = screen.getByRole("link", { name: "New conversation" });
    expect(untitled).toHaveAttribute("href", "/chat/conv-1");
    expect(untitled).not.toHaveAttribute("aria-current");
  });

  it("starts a conversation and opens it", async () => {
    const user = userEvent.setup();
    renderList();

    await user.click(screen.getByRole("button", { name: "New conversation" }));

    await waitFor(() => expect(push).toHaveBeenCalledWith("/chat/conv-new"));
    expect(
      fetchMock.mock.calls.some(
        ([url, init]) =>
          url === "/api/proxy/conversations" && init?.method === "POST",
      ),
    ).toBe(true);
  });
});
