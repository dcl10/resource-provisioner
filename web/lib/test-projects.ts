import type { VariableSpec } from "@/lib/catalog";
import type { ProjectDetail } from "@/lib/projects";
import type { RequestStatus } from "@/lib/requests";
import { testRequest } from "@/lib/test-requests";

/**
 * A project for tests: `shop`, owned by user-1, with its `project` request and no resources.
 * @param status - its status
 * @param overrides - fields to change
 * @returns the project
 */
export function testProject(
  status: RequestStatus,
  overrides: Partial<ProjectDetail> = {},
): ProjectDetail {
  return {
    id: "prj-1",
    ownerId: "user-1",
    ownerEmail: "user@example.com",
    name: "shop",
    description: "The online shop",
    purpose: "Web Application",
    environment: "Development",
    status,
    projectRequestId: "req-p",
    resourceCount: 0,
    createdAt: "2026-11-07T09:00:00Z",
    request: testRequest(status, {
      id: "req-p",
      type: "project",
      displayName: "Project",
      variables: { name: "shop" },
      project: { id: "prj-1", name: "shop" },
    }),
    resources: [],
    ...overrides,
  };
}

/** The `project` type's variables, as its manifest has them. */
export const projectVariables: VariableSpec[] = [
  {
    name: "name",
    label: "Name",
    type: "string",
    description: "Project name: lowercase letters, digits and hyphens.",
    required: true,
    sensitive: false,
    pattern: "^[a-z][a-z0-9-]*$",
    maxLength: 54,
  },
  {
    name: "description",
    label: "Description",
    type: "string",
    description: "What the project is for. Optional.",
    required: false,
    sensitive: false,
    maxLength: 255,
    default: "",
  },
  {
    name: "environment",
    label: "Environment",
    type: "string",
    description: "Which environment the project's resources are.",
    required: false,
    sensitive: false,
    allowedValues: ["Development", "Staging", "Production"],
    default: "Development",
  },
];
