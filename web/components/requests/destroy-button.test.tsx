import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";

import { Providers } from "@/components/providers";
import { DestroyButton } from "@/components/requests/destroy-button";
import { jsonResponse, testRequest } from "@/lib/test-requests";

let fetchMock: ReturnType<typeof vi.fn>;

function mockDestroy(response: () => Response) {
  fetchMock = vi.fn(async () => response());
  vi.stubGlobal("fetch", fetchMock);
}

function renderButton() {
  return render(
    <Providers>
      <DestroyButton request={testRequest("Succeeded")} />
    </Providers>,
  );
}

describe("DestroyButton", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("asks for confirmation before destroying", async () => {
    mockDestroy(() => jsonResponse(testRequest("Destroying")));
    const user = userEvent.setup();
    renderButton();

    await user.click(screen.getByRole("button", { name: "Destroy" }));

    const dialog = await screen.findByRole("alertdialog");
    expect(
      within(dialog).getByText('Destroy Droplet "web-1"?'),
    ).toBeInTheDocument();
    expect(within(dialog).getByText(/can't be undone/)).toBeInTheDocument();
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("does nothing when the user keeps it", async () => {
    mockDestroy(() => jsonResponse(testRequest("Destroying")));
    const user = userEvent.setup();
    renderButton();

    await user.click(screen.getByRole("button", { name: "Destroy" }));
    await user.click(await screen.findByRole("button", { name: "Keep it" }));

    await waitFor(() =>
      expect(screen.queryByRole("alertdialog")).not.toBeInTheDocument(),
    );
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("destroys after confirmation and says so", async () => {
    mockDestroy(() => jsonResponse(testRequest("Destroying")));
    const user = userEvent.setup();
    renderButton();

    await user.click(screen.getByRole("button", { name: "Destroy" }));
    const dialog = await screen.findByRole("alertdialog");
    await user.click(within(dialog).getByRole("button", { name: "Destroy" }));

    expect(
      await screen.findByText('Destroying Droplet "web-1"'),
    ).toBeInTheDocument();
    expect(fetchMock).toHaveBeenCalledWith(
      "/api/proxy/requests/req-1/destroy",
      expect.objectContaining({ method: "POST" }),
    );
  });

  it("shows the API's message when destroy is refused", async () => {
    mockDestroy(() =>
      jsonResponse(
        { detail: "This request is Destroying, so it can't be destroyed." },
        409,
      ),
    );
    const user = userEvent.setup();
    renderButton();

    await user.click(screen.getByRole("button", { name: "Destroy" }));
    const dialog = await screen.findByRole("alertdialog");
    await user.click(within(dialog).getByRole("button", { name: "Destroy" }));

    expect(
      await screen.findByText(
        "This request is Destroying, so it can't be destroyed.",
      ),
    ).toBeInTheDocument();
  });
});
