"use client";

import { useEffect, useRef, useState } from "react";

import { followLogs, type LogLine, type RequestStatus } from "@/lib/requests";
import { cn } from "@/lib/utils";

/** Where the log stream has got to. */
type LogState = "live" | "ended" | "error";

/**
 * Follows a request's Terraform log. It reconnects whenever `follow` changes, resuming after the last line it has, so
 * a destroy started from the page streams in below the apply.
 * @param requestId - the request
 * @param follow - whether a job is queued or running; pass the request's in-progress state
 * @param onStatus - called when the stream reports a status, to refresh the request
 * @returns the lines so far and the stream's state
 */
export function useRequestLog(
  requestId: string,
  follow: boolean,
  onStatus: (status: RequestStatus) => void,
) {
  const [lines, setLines] = useState<LogLine[]>([]);
  const [state, setState] = useState<LogState>("live");
  const lastId = useRef(0);
  const statusCallback = useRef(onStatus);

  useEffect(() => {
    statusCallback.current = onStatus;
  }, [onStatus]);

  useEffect(() => {
    const controller = new AbortController();
    followLogs(
      requestId,
      lastId.current,
      {
        onLine: (line) => {
          if (line.id <= lastId.current) return;
          lastId.current = line.id;
          setLines((current) => [...current, line]);
        },
        onStatus: (status) => {
          setState("live");
          statusCallback.current(status);
        },
      },
      controller.signal,
    ).then(
      () => setState("ended"),
      () => {
        if (!controller.signal.aborted) setState("error");
      },
    );
    return () => controller.abort();
  }, [requestId, follow]);

  return { lines, state };
}

/**
 * A request's Terraform log, streamed live while a job runs.
 * @param requestId - the request
 * @param follow - whether a job is queued or running
 * @param onStatus - called when the stream reports a status
 * @returns the log panel
 */
export function RequestLog({
  requestId,
  follow,
  onStatus,
}: {
  requestId: string;
  follow: boolean;
  onStatus: (status: RequestStatus) => void;
}) {
  const { lines, state } = useRequestLog(requestId, follow, onStatus);
  const panel = useRef<HTMLDivElement>(null);

  useEffect(() => {
    const element = panel.current;
    if (element) element.scrollTop = element.scrollHeight;
  }, [lines]);

  return (
    <div className="flex flex-col gap-2">
      <div
        ref={panel}
        role="log"
        aria-label="Terraform log"
        aria-live="polite"
        className="max-h-96 min-h-24 overflow-y-auto rounded-md bg-muted p-3 font-mono text-xs leading-relaxed"
      >
        {lines.length === 0 ? (
          <p className="text-muted-foreground">
            {follow ? "Waiting for the runner…" : "No log yet."}
          </p>
        ) : (
          lines.map((line) => (
            <div
              key={line.id}
              data-stream={line.stream}
              className={cn(
                "break-all whitespace-pre-wrap",
                line.stream === "System" && "font-semibold",
                line.stream === "Stderr" && "text-destructive",
              )}
            >
              {line.text || " "}
            </div>
          ))
        )}
      </div>
      {state === "error" ? (
        <p role="alert" className="text-sm text-muted-foreground">
          Lost the connection to the log. Reload the page to try again.
        </p>
      ) : null}
    </div>
  );
}
