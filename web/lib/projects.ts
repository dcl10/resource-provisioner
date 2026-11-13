import { z } from "zod";

import { apiFetch } from "@/lib/api";
import type { VariableSpec } from "@/lib/catalog";
import type {
  ProvisioningRequest,
  RequestLink,
  RequestStatus,
} from "@/lib/requests";

/** The catalog type whose requests back projects. Its manifest drives the New project form. */
export const PROJECT_TYPE = "project";

/** A project as `GET /api/projects` returns it. Dates are ISO 8601. */
export interface Project {
  id: string;
  ownerId: string;
  ownerEmail: string | null;
  name: string;
  description: string | null;
  purpose: string | null;
  environment: string | null;
  /** The status of the `project` request that backs it. */
  status: RequestStatus;
  /** The `project` request that backs it: its log, outputs and destroy are that request's. */
  projectRequestId: string;
  /** How many requests belong to it. */
  resourceCount: number;
  createdAt: string;
}

/** A project as `GET /api/projects/{id}` returns it, with its `project` request and the requests in it. */
export interface ProjectDetail extends Project {
  request: ProvisioningRequest;
  /** Every request in it, newest first, each with its `dependsOn` and `dependents`. */
  resources: ProvisioningRequest[];
}

/**
 * The page of a project.
 * @param id - the project ID
 * @returns its path
 */
export function projectPath(id: string): string {
  return `/projects/${encodeURIComponent(id)}`;
}

/**
 * Lists the user's projects, or everyone's for an admin, newest first.
 * @returns the projects
 */
export function listProjects(): Promise<Project[]> {
  return apiFetch<Project[]>("/projects");
}

/**
 * Reads one project, with its `project` request and its resources.
 * @param id - the project ID
 * @returns the project
 */
export function getProject(id: string): Promise<ProjectDetail> {
  return apiFetch<ProjectDetail>(`/projects/${encodeURIComponent(id)}`);
}

/**
 * Deletes a project: a cascading destroy of its `project` request, so everything in it is destroyed first and every
 * proposal in it cancelled. Once the runner is done the project is gone (404). Owner or admin; 409 for a project still
 * awaiting confirmation, or while something in it is still running. The plan is its `project` request's destroy plan.
 * @param id - the project ID
 * @returns the project, now Destroying
 */
export function deleteProject(id: string): Promise<ProjectDetail> {
  return apiFetch<ProjectDetail>(`/projects/${encodeURIComponent(id)}`, {
    method: "DELETE",
  });
}

/**
 * Creates a project: proposes its `project` request and confirms it in one step, since submitting the form is the
 * user's confirmation. Fails with 422 when the variables aren't valid, or 409 over a limit.
 * @param variables - the `project` request's variables, by name
 * @returns the new project
 */
export function createProject(
  variables: Record<string, unknown>,
): Promise<ProjectDetail> {
  return apiFetch<ProjectDetail>("/projects", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ variables }),
  });
}

/** One of a project's resources, with what depends on it nested under it. */
export interface ResourceNode {
  request: ProvisioningRequest;
  /** Resources in the project that depend on this one, and are shown under it. */
  children: ResourceNode[];
  /** Other resources in the project it depends on too, beyond the one it's shown under. */
  alsoAfter: RequestLink[];
}

/**
 * Arranges a project's resources as a tree: each one under the first resource in the project it depends on, or at
 * the top if it depends on none (everything depends on the `project` request, which isn't one of them). Order is
 * kept within each level. The API never lets dependencies form a cycle, so every resource is reached.
 * @param resources - the project's resources
 * @returns the top-level nodes
 */
export function resourceTree(resources: ProvisioningRequest[]): ResourceNode[] {
  const ids = new Set(resources.map((r) => r.id));
  const nodes = new Map<string, ResourceNode>();
  const parents = new Map<string, string>();
  for (const request of resources) {
    const deps = request.dependsOn.filter((d) => ids.has(d.id));
    nodes.set(request.id, { request, children: [], alsoAfter: deps.slice(1) });
    if (deps.length > 0) parents.set(request.id, deps[0].id);
  }

  const roots: ResourceNode[] = [];
  for (const request of resources) {
    const node = nodes.get(request.id)!;
    const parent = parents.get(request.id);
    if (parent === undefined) roots.push(node);
    else nodes.get(parent)!.children.push(node);
  }
  return roots;
}

/**
 * The variables a form collects: plain strings the user types or picks. Secrets and references to other requests are
 * left out; the server applies defaults to anything left out.
 * @param variables - a type's variables, from its manifest
 * @returns the ones to show as fields, in manifest order
 */
export function formVariables(variables: VariableSpec[]): VariableSpec[] {
  return variables.filter(
    (v) => v.type === "string" && !v.sensitive && !v.references,
  );
}

/**
 * A field's label: the manifest's, or the variable's name.
 * @param variable - the variable
 * @returns the label
 */
export function variableLabel(variable: VariableSpec): string {
  return variable.label ?? variable.name;
}

/** A form's values: each field's text, by variable name. */
export type VariableValues = Record<string, string>;

function stringSchema(variable: VariableSpec) {
  const label = variableLabel(variable);
  const allowed = variable.allowedValues?.map(String);
  let pattern: RegExp | undefined;
  try {
    pattern = variable.pattern ? new RegExp(variable.pattern) : undefined;
  } catch {
    // A .NET pattern JavaScript can't read: leave it to the server.
  }

  let schema = z.string().trim();
  if (variable.required)
    schema = schema.min(1, `Enter a ${label.toLowerCase()}.`);
  if (variable.maxLength !== undefined) {
    schema = schema.max(
      variable.maxLength,
      `${label} can be at most ${variable.maxLength} characters.`,
    );
  }
  // An empty optional field is left out of the request, so only a value is checked against the rest.
  return schema
    .refine(
      (v) =>
        v === "" ||
        variable.minLength === undefined ||
        v.length >= variable.minLength,
      `${label} must be at least ${variable.minLength} characters.`,
    )
    .refine(
      (v) => v === "" || !pattern || pattern.test(v),
      `${label} isn't in the right format.`,
    )
    .refine(
      (v) => v === "" || !allowed || allowed.includes(v),
      `Pick one of the ${label.toLowerCase()} options.`,
    );
}

/**
 * A zod schema for a form over a type's variables, from its manifest: required, length, pattern and allowed values,
 * the same rules the API checks again.
 * @param variables - the fields, from {@link formVariables}
 * @returns the schema
 */
export function variablesSchema(
  variables: VariableSpec[],
): z.ZodType<VariableValues, VariableValues> {
  return z.object(
    Object.fromEntries(variables.map((v) => [v.name, stringSchema(v)])),
  ) as unknown as z.ZodType<VariableValues, VariableValues>;
}

/**
 * A form's starting values: each variable's default from the manifest, or empty.
 * @param variables - the fields
 * @returns the values
 */
export function defaultVariableValues(
  variables: VariableSpec[],
): VariableValues {
  return Object.fromEntries(
    variables.map((v) => [
      v.name,
      typeof v.default === "string" || typeof v.default === "number"
        ? String(v.default)
        : "",
    ]),
  );
}

/**
 * The variables to send for a form's values: trimmed, with empty fields left out so the server applies defaults.
 * @param values - the form's values
 * @returns the variables, by name
 */
export function toVariables(values: VariableValues): Record<string, string> {
  return Object.fromEntries(
    Object.entries(values)
      .map(([name, value]) => [name, value.trim()] as const)
      .filter(([, value]) => value !== ""),
  );
}
