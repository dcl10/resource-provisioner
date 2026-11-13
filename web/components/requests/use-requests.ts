"use client";

import { useQuery } from "@tanstack/react-query";

import { retryUnlessClientError } from "@/lib/api";
import { listResourceTypes } from "@/lib/catalog";
import {
  getDestroyPlan,
  getRequest,
  isInProgress,
  listRequests,
  type ProvisioningRequest,
} from "@/lib/requests";

/** How often a request with a job queued or running is re-read. */
export const POLL_INTERVAL_MS = 3000;

/** TanStack Query key for the requests list. */
export const requestsQueryKey = ["requests"] as const;

/**
 * TanStack Query key for one request. Starts with {@link requestsQueryKey}, so invalidating the list refreshes it too.
 * @param id - the request ID
 * @returns the key
 */
export function requestQueryKey(id: string) {
  return ["requests", id] as const;
}

/** TanStack Query key for the catalog. */
export const catalogQueryKey = ["catalog"] as const;

/**
 * The user's requests (everyone's for an admin), re-read every few seconds while any has a job queued or running.
 * @returns the query
 */
export function useRequests() {
  return useQuery({
    queryKey: requestsQueryKey,
    queryFn: listRequests,
    refetchInterval: (query) =>
      query.state.data?.some((r: ProvisioningRequest) => isInProgress(r.status))
        ? POLL_INTERVAL_MS
        : false,
  });
}

/**
 * One request, re-read every few seconds while it has a job queued or running.
 * @param id - the request ID
 * @returns the query
 */
export function useRequest(id: string) {
  return useQuery({
    queryKey: requestQueryKey(id),
    queryFn: () => getRequest(id),
    retry: retryUnlessClientError,
    refetchInterval: (query) =>
      query.state.data && isInProgress(query.state.data.status)
        ? POLL_INTERVAL_MS
        : false,
  });
}

/**
 * TanStack Query key for what destroying a request would do. Starts with {@link requestQueryKey}, so refreshing the
 * request refreshes it too.
 * @param id - the request ID
 * @returns the key
 */
export function destroyPlanQueryKey(id: string) {
  return ["requests", id, "destroy-plan"] as const;
}

/**
 * What destroying a request would do, read afresh whenever something shows it (a destroy dialog opening) and dropped
 * once nothing does. While something that depends on the request is still running, it's re-read every few seconds,
 * so the destroy is offered as soon as that has finished.
 * @param id - the request ID
 * @returns the query
 */
export function useDestroyPlan(id: string) {
  return useQuery({
    queryKey: destroyPlanQueryKey(id),
    queryFn: () => getDestroyPlan(id),
    retry: retryUnlessClientError,
    gcTime: 0,
    refetchInterval: (query) =>
      query.state.data && query.state.data.waitFor.length > 0
        ? POLL_INTERVAL_MS
        : false,
  });
}

/**
 * The catalog's resource types, which rarely change.
 * @returns the query
 */
export function useCatalog() {
  return useQuery({
    queryKey: catalogQueryKey,
    queryFn: listResourceTypes,
    staleTime: Infinity,
  });
}
