import { apiFetch } from "@/lib/api";

/** One of a resource type's Terraform outputs. */
export interface OutputSpec {
  name: string;
  description: string;
  /** A secret, shown only through the owner's reveal. */
  sensitive?: boolean;
}

/** A variable's type, in JSON Schema's names. */
export type VariableType =
  "string" | "integer" | "number" | "boolean" | "array" | "object";

/** One of a resource type's variables, as its manifest describes it, reduced to what the UI reads. */
export interface VariableSpec {
  name: string;
  /** A short human label; falls back to the name. */
  label?: string;
  type: VariableType;
  description?: string;
  required: boolean;
  default?: unknown;
  /** A secret: never collected by a form here. */
  sensitive: boolean;
  /** The only values accepted. */
  allowedValues?: (string | number)[];
  /** A regular expression a string must match. */
  pattern?: string;
  minLength?: number;
  maxLength?: number;
  /** The catalog type whose request IDs this holds, if it references other requests. */
  references?: string;
}

/** A resource type as `GET /api/catalog` returns it, reduced to what the UI reads. */
export interface ResourceType {
  id: string;
  displayName: string;
  description: string;
  costHint: string;
  variables: VariableSpec[];
  outputs: OutputSpec[];
}

/**
 * Lists every resource type in the catalog.
 * @returns the types
 */
export function listResourceTypes(): Promise<ResourceType[]> {
  return apiFetch<ResourceType[]>("/catalog");
}
