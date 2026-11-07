"use client";

import { SendHorizontal } from "lucide-react";
import type { FormEvent, KeyboardEvent } from "react";

import { Button } from "@/components/ui/button";
import { Textarea } from "@/components/ui/textarea";
import { MAX_MESSAGE_LENGTH } from "@/lib/chat";

/**
 * The message box under the chat. Enter sends; Shift+Enter starts a new line.
 * @param value - the text typed so far
 * @param onChange - called as the user types
 * @param onSend - called with the trimmed text when the user sends
 * @param disabled - whether sending is blocked (a reply is streaming)
 * @returns the form
 */
export function Composer({
  value,
  onChange,
  onSend,
  disabled,
}: {
  value: string;
  onChange: (value: string) => void;
  onSend: (text: string) => void;
  disabled: boolean;
}) {
  const text = value.trim();
  const canSend = !disabled && text.length > 0;

  const submit = (event?: FormEvent) => {
    event?.preventDefault();
    if (canSend) onSend(text);
  };

  const onKeyDown = (event: KeyboardEvent<HTMLTextAreaElement>) => {
    if (
      event.key === "Enter" &&
      !event.shiftKey &&
      !event.nativeEvent.isComposing
    ) {
      submit(event);
    }
  };

  return (
    <form onSubmit={submit} className="flex items-end gap-2">
      <Textarea
        aria-label="Message"
        placeholder="Describe what you need, for example: a small Postgres database in London"
        value={value}
        maxLength={MAX_MESSAGE_LENGTH}
        rows={2}
        className="max-h-48 min-h-11 resize-none"
        onChange={(event) => onChange(event.target.value)}
        onKeyDown={onKeyDown}
      />
      <Button type="submit" size="lg" disabled={!canSend} aria-label="Send">
        <SendHorizontal />
      </Button>
    </form>
  );
}
