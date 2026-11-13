import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { NewProjectDialog } from "@/components/projects/new-project-dialog";
import { Providers } from "@/components/providers";
import { projectVariables, testProject } from "@/lib/test-projects";
import { jsonResponse } from "@/lib/test-requests";

const push = vi.fn();
vi.mock("next/navigation", () => ({ useRouter: () => ({ push }) }));

const catalog = [
  {
    id: "project",
    displayName: "Project",
    description: "A DigitalOcean project.",
    costHint: "Free.",
    variables: projectVariables,
    outputs: [],
  },
];

let fetchMock: ReturnType<typeof vi.fn>;

function mockApi(create: () => Response) {
  fetchMock = vi.fn(async (url: string, init?: RequestInit) =>
    init?.method === "POST"
      ? create()
      : url.endsWith("/catalog")
        ? jsonResponse(catalog)
        : jsonResponse([]),
  );
  vi.stubGlobal("fetch", fetchMock);
}

function posted() {
  return fetchMock.mock.calls
    .filter(([, init]) => init?.method === "POST")
    .map(([url, init]) => ({
      url: url as string,
      body: JSON.parse((init as RequestInit).body as string) as unknown,
    }));
}

async function openForm() {
  const user = userEvent.setup();
  render(
    <Providers>
      <NewProjectDialog />
    </Providers>,
  );
  await user.click(screen.getByRole("button", { name: "New project" }));
  await screen.findByLabelText(/^Name/);
  return user;
}

describe("NewProjectDialog", () => {
  beforeEach(() => {
    push.mockReset();
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("builds its fields from the project manifest, with its defaults", async () => {
    mockApi(() => jsonResponse(testProject("Queued"), 201));
    await openForm();

    expect(screen.getByLabelText(/^Name/)).toHaveValue("");
    expect(screen.getByLabelText(/^Description/)).toHaveValue("");
    expect(
      screen.getByRole("combobox", { name: /^Environment/ }),
    ).toHaveTextContent("Development");
    expect(
      screen.getByText("Project name: lowercase letters, digits and hyphens."),
    ).toBeInTheDocument();
  });

  it("checks the manifest's rules before sending anything", async () => {
    mockApi(() => jsonResponse(testProject("Queued"), 201));
    const user = await openForm();

    await user.click(screen.getByRole("button", { name: "Create project" }));
    expect(await screen.findByText("Enter a name.")).toBeInTheDocument();

    await user.type(screen.getByLabelText(/^Name/), "My Shop");
    await user.click(screen.getByRole("button", { name: "Create project" }));
    expect(
      await screen.findByText("Name isn't in the right format."),
    ).toBeInTheDocument();
    expect(posted()).toEqual([]);
  });

  it("creates the project and opens its page", async () => {
    mockApi(() => jsonResponse(testProject("Queued"), 201));
    const user = await openForm();

    await user.type(screen.getByLabelText(/^Name/), "shop");
    await user.click(screen.getByRole("combobox", { name: /^Environment/ }));
    await user.click(await screen.findByRole("option", { name: "Staging" }));
    await user.click(screen.getByRole("button", { name: "Create project" }));

    await waitFor(() => expect(push).toHaveBeenCalledWith("/projects/prj-1"));
    expect(posted()).toEqual([
      {
        url: "/api/proxy/projects",
        body: { variables: { name: "shop", environment: "Staging" } },
      },
    ]);
  });

  it("shows the API's error and stays open", async () => {
    mockApi(() =>
      jsonResponse(
        {
          title: "Invalid variables",
          errors: { name: ["A project named shop already exists."] },
        },
        422,
      ),
    );
    const user = await openForm();

    await user.type(screen.getByLabelText(/^Name/), "shop");
    await user.click(screen.getByRole("button", { name: "Create project" }));

    expect(await screen.findByRole("alert")).toHaveTextContent(
      "A project named shop already exists.",
    );
    expect(push).not.toHaveBeenCalled();
    expect(screen.getByLabelText(/^Name/)).toBeInTheDocument();
  });
});
