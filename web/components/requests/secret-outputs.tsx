"use client";

import { useQuery, useQueryClient } from "@tanstack/react-query";
import { Eye, EyeOff } from "lucide-react";
import { useState } from "react";

import { Button } from "@/components/ui/button";
import { Table, TableBody, TableCell, TableRow } from "@/components/ui/table";
import type { OutputSpec } from "@/lib/catalog";
import { formatValue, getSensitiveOutputs } from "@/lib/requests";

/**
 * The request's secret outputs, hidden until the owner asks to see them. Hiding them again drops them from the query
 * cache, and they're never cached by the browser.
 * @param requestId - the request
 * @param outputs - the type's secret outputs, for their names and descriptions
 * @returns the reveal toggle and, once revealed, the values
 */
export function SecretOutputs({
  requestId,
  outputs,
}: {
  requestId: string;
  outputs: OutputSpec[];
}) {
  const queryClient = useQueryClient();
  const [revealed, setRevealed] = useState(false);
  const queryKey = ["requests", requestId, "sensitive-outputs"];
  const secrets = useQuery({
    queryKey,
    queryFn: () => getSensitiveOutputs(requestId),
    enabled: revealed,
    staleTime: 0,
    gcTime: 0,
  });

  const hide = () => {
    setRevealed(false);
    queryClient.removeQueries({ queryKey });
  };

  return (
    <div className="flex flex-col gap-2">
      <div className="flex items-center justify-between gap-2">
        <p className="text-sm text-muted-foreground">
          Passwords and keys stay hidden until you reveal them.
        </p>
        {revealed ? (
          <Button variant="outline" size="sm" onClick={hide}>
            <EyeOff />
            Hide secrets
          </Button>
        ) : (
          <Button variant="outline" size="sm" onClick={() => setRevealed(true)}>
            <Eye />
            Reveal secrets
          </Button>
        )}
      </div>
      <Table>
        <TableBody>
          {outputs.map((output) => (
            <TableRow key={output.name}>
              <TableCell className="w-1/3 align-top">
                <div className="font-mono text-xs">{output.name}</div>
                <div className="text-xs text-muted-foreground">
                  {output.description}
                </div>
              </TableCell>
              <TableCell className="font-mono text-xs break-all whitespace-pre-wrap">
                {!revealed
                  ? "••••••••"
                  : secrets.isPending
                    ? "Loading…"
                    : secrets.isError
                      ? secrets.error.message
                      : formatValue(secrets.data[output.name])}
              </TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>
    </div>
  );
}
