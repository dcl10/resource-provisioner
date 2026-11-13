"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";

import {
  POLL_INTERVAL_MS,
  requestsQueryKey,
} from "@/components/requests/use-requests";
import { retryUnlessClientError } from "@/lib/api";
import {
  createProject,
  getProject,
  listProjects,
  type Project,
  type ProjectDetail,
} from "@/lib/projects";
import { isInProgress } from "@/lib/requests";

/** TanStack Query key for the projects list. */
export const projectsQueryKey = ["projects"] as const;

/**
 * TanStack Query key for one project. Starts with {@link projectsQueryKey}, so invalidating the list refreshes it too.
 * @param id - the project ID
 * @returns the key
 */
export function projectQueryKey(id: string) {
  return ["projects", id] as const;
}

/**
 * The user's projects (everyone's for an admin), re-read every few seconds while any has a job queued or running.
 * @returns the query
 */
export function useProjects() {
  return useQuery({
    queryKey: projectsQueryKey,
    queryFn: listProjects,
    refetchInterval: (query) =>
      query.state.data?.some((p: Project) => isInProgress(p.status))
        ? POLL_INTERVAL_MS
        : false,
  });
}

/**
 * One project with its resources, re-read every few seconds while it or any of its resources has a job queued or
 * running.
 * @param id - the project ID
 * @returns the query
 */
export function useProject(id: string) {
  return useQuery({
    queryKey: projectQueryKey(id),
    queryFn: () => getProject(id),
    retry: retryUnlessClientError,
    refetchInterval: (query) => {
      const project: ProjectDetail | undefined = query.state.data;
      return project &&
        (isInProgress(project.status) ||
          project.resources.some((r) => isInProgress(r.status)))
        ? POLL_INTERVAL_MS
        : false;
    },
  });
}

/**
 * Creates a project from the New project form. On success it refreshes the projects and requests lists, which both
 * now hold it.
 * @returns the mutation
 */
export function useCreateProject() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: createProject,
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: projectsQueryKey });
      void queryClient.invalidateQueries({
        queryKey: requestsQueryKey,
        exact: true,
      });
    },
  });
}
