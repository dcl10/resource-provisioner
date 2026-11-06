import { afterEach, describe, expect, it, vi } from "vitest";

import {
  ApiError,
  inviteSchema,
  inviteUser,
  isOpen,
  readErrorMessage,
} from "@/lib/invitations";

function jsonResponse(body: unknown, status: number) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": "application/problem+json" },
  });
}

describe("inviteSchema", () => {
  it("trims a valid email", () => {
    expect(inviteSchema.parse({ email: "  a@example.com " })).toEqual({
      email: "a@example.com",
    });
  });

  it.each([
    ["", "Enter an email address."],
    ["not-an-email", "Enter a valid email address."],
  ])("rejects %j with a message", (email, message) => {
    const result = inviteSchema.safeParse({ email });

    expect(result.success).toBe(false);
    expect(result.error?.issues[0].message).toBe(message);
  });
});

describe("isOpen", () => {
  it.each([
    ["Pending", true],
    ["Expired", true],
    ["Accepted", false],
    ["Revoked", false],
  ] as const)("returns %s -> %s", (status, open) => {
    expect(isOpen(status)).toBe(open);
  });
});

describe("readErrorMessage", () => {
  it("uses the problem's detail", async () => {
    const message = await readErrorMessage(
      jsonResponse({ detail: "a@example.com already has an account." }, 409),
    );

    expect(message).toBe("a@example.com already has an account.");
  });

  it("uses the first validation error when there is no detail", async () => {
    const message = await readErrorMessage(
      jsonResponse(
        {
          title: "One or more validation errors occurred.",
          errors: { Email: ["The Email field is not a valid e-mail address."] },
        },
        400,
      ),
    );

    expect(message).toBe("The Email field is not a valid e-mail address.");
  });

  it("explains a 403 without reading the body", async () => {
    const message = await readErrorMessage(new Response(null, { status: 403 }));

    expect(message).toBe("Only admins can manage invitations.");
  });

  it("falls back to the status when the body isn't JSON", async () => {
    const message = await readErrorMessage(
      new Response("oops", { status: 500 }),
    );

    expect(message).toBe("Request failed (500).");
  });
});

describe("inviteUser", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("posts the email to the API proxy", async () => {
    const fetchMock = vi
      .fn()
      .mockResolvedValue(
        jsonResponse({ id: "1", email: "a@example.com" }, 201),
      );
    vi.stubGlobal("fetch", fetchMock);

    await inviteUser({ email: "a@example.com" });

    expect(fetchMock).toHaveBeenCalledWith(
      "/api/proxy/invitations",
      expect.objectContaining({
        method: "POST",
        body: JSON.stringify({ email: "a@example.com" }),
      }),
    );
  });

  it("throws an ApiError with the API's message when it fails", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue(jsonResponse({ detail: "Nope." }, 502)),
    );

    await expect(inviteUser({ email: "a@example.com" })).rejects.toEqual(
      new ApiError("Nope.", 502),
    );
  });
});
