import { describe, expect, it } from "vitest";

import {
  defaultVariableValues,
  formVariables,
  resourceTree,
  toVariables,
  variablesSchema,
} from "@/lib/projects";
import type { RequestLink } from "@/lib/requests";
import { projectVariables } from "@/lib/test-projects";
import { testRequest } from "@/lib/test-requests";

function link(id: string): RequestLink {
  return {
    id,
    type: "droplet",
    displayName: "Droplet",
    name: id,
    status: "Succeeded",
  };
}

describe("resourceTree", () => {
  it("nests each resource under the first resource in the project it depends on", () => {
    const projectRequest = link("req-p");
    const web = testRequest("Succeeded", {
      id: "web",
      dependsOn: [projectRequest],
    });
    const db = testRequest("Succeeded", {
      id: "db",
      dependsOn: [projectRequest],
    });
    const firewall = testRequest("Succeeded", {
      id: "fw",
      dependsOn: [projectRequest, link("web"), link("db")],
    });

    const tree = resourceTree([firewall, db, web]);

    expect(tree.map((n) => n.request.id)).toEqual(["db", "web"]);
    const webNode = tree.find((n) => n.request.id === "web")!;
    expect(webNode.children.map((n) => n.request.id)).toEqual(["fw"]);
    expect(webNode.children[0].alsoAfter.map((l) => l.id)).toEqual(["db"]);
    expect(tree.find((n) => n.request.id === "db")!.children).toEqual([]);
  });
});

describe("variablesSchema", () => {
  const schema = variablesSchema(projectVariables);
  const valid = { name: "shop", description: "", environment: "Development" };

  it("accepts values that follow the manifest", () => {
    expect(schema.safeParse(valid).success).toBe(true);
  });

  it.each([
    [{ ...valid, name: "" }, "Enter a name."],
    [{ ...valid, name: "Shop" }, "Name isn't in the right format."],
    [{ ...valid, name: "a".repeat(55) }, "Name can be at most 54 characters."],
    [
      { ...valid, environment: "Testing" },
      "Pick one of the environment options.",
    ],
  ])("rejects %o with %s", (values, message) => {
    const result = schema.safeParse(values);
    expect(result.success).toBe(false);
    expect(result.error?.issues[0].message).toBe(message);
  });
});

describe("formVariables", () => {
  it("leaves out secrets, references and anything but strings", () => {
    const fields = formVariables([
      ...projectVariables,
      { name: "password", type: "string", required: false, sensitive: true },
      {
        name: "droplet_ids",
        type: "array",
        required: false,
        sensitive: false,
        references: "droplet",
      },
      { name: "size", type: "integer", required: false, sensitive: false },
    ]);

    expect(fields.map((v) => v.name)).toEqual([
      "name",
      "description",
      "environment",
    ]);
  });
});

describe("defaultVariableValues and toVariables", () => {
  it("start from the manifest's defaults and send only what's filled in", () => {
    const values = defaultVariableValues(projectVariables);
    expect(values).toEqual({
      name: "",
      description: "",
      environment: "Development",
    });

    expect(toVariables({ ...values, name: " shop " })).toEqual({
      name: "shop",
      environment: "Development",
    });
  });
});
