import { apiFetch } from "@/lib/api";

/** One of a resource type's Terraform outputs. */
export interface OutputSpec {
  name: string;
  description: string;
  /** A secret, shown only through the owner's reveal. */
  sensitive?: boolean;
}

/** A resource type as `GET /api/catalog` returns it, reduced to what the UI reads. */
export interface ResourceType {
  id: string;
  displayName: string;
  description: string;
  costHint: string;
  outputs: OutputSpec[];
}

/**
 * Lists every resource type in the catalog.
 * @returns the types
 */
export function listResourceTypes(): Promise<ResourceType[]> {
  return apiFetch<ResourceType[]>("/catalog");
}
