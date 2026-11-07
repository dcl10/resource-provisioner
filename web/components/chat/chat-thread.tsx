"use client";

import { useQueryClient } from "@tanstack/react-query";
import { useEffect, useRef, useState } from "react";

import { Composer } from "@/components/chat/composer";
import { ConfirmAllButton } from "@/components/chat/confirm-all-button";
import { Markdown } from "@/components/chat/markdown";
import { ProposalCard } from "@/components/chat/proposal-card";
import {
  conversationQueryKey,
  conversationsQueryKey,
} from "@/components/chat/use-chat";
import { ApiError } from "@/lib/api";
import { type ChatMessage, sendMessage } from "@/lib/chat";
import { cn } from "@/lib/utils";

/**
 * A conversation's messages and the composer. Sending appends the user's message, then streams the agent's reply into
 * a new assistant message, with a confirmation card for each request it proposes.
 * @param conversationId - the conversation
 * @param initialMessages - the messages already in it
 * @returns the thread
 */
export function ChatThread({
  conversationId,
  initialMessages,
}: {
  conversationId: string;
  initialMessages: ChatMessage[];
}) {
  const queryClient = useQueryClient();
  const [messages, setMessages] = useState(initialMessages);
  const [draft, setDraft] = useState("");
  const [streaming, setStreaming] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const abort = useRef<AbortController | null>(null);
  const end = useRef<HTMLDivElement>(null);

  useEffect(() => () => abort.current?.abort(), []);
  useEffect(() => {
    end.current?.scrollIntoView?.({ block: "end" });
  }, [messages, error]);

  const updateReply = (change: (reply: ChatMessage) => ChatMessage) =>
    setMessages((current) => [
      ...current.slice(0, -1),
      change(current.at(-1)!),
    ]);

  const send = async (text: string) => {
    const controller = new AbortController();
    abort.current = controller;
    setError(null);
    setDraft("");
    setStreaming(true);
    setMessages((current) => [
      ...current,
      { id: crypto.randomUUID(), role: "user", text, proposals: [] },
      { id: crypto.randomUUID(), role: "assistant", text: "", proposals: [] },
    ]);

    try {
      await sendMessage(
        conversationId,
        text,
        {
          onText: (delta) =>
            updateReply((reply) => ({ ...reply, text: reply.text + delta })),
          onProposal: (proposal) =>
            updateReply((reply) => ({
              ...reply,
              proposals: [...reply.proposals, proposal],
            })),
        },
        controller.signal,
      );
    } catch (caught) {
      if (controller.signal.aborted) return;
      const refused = caught instanceof ApiError && caught.status !== 0;
      setMessages((current) => {
        // A refused message (rate limit, 403, 404) was never sent: take it back and put it in the box again.
        if (refused) return current.slice(0, -2);
        const reply = current.at(-1)!;
        return reply.text || reply.proposals.length
          ? current
          : current.slice(0, -1);
      });
      if (refused) setDraft(text);
      setError(
        caught instanceof ApiError
          ? caught.message
          : "Couldn't reach the app. Check your connection and try again.",
      );
    } finally {
      if (!controller.signal.aborted) {
        setStreaming(false);
        void queryClient.invalidateQueries({ queryKey: conversationsQueryKey });
        void queryClient.invalidateQueries({
          queryKey: conversationQueryKey(conversationId),
          exact: true,
          refetchType: "none",
        });
      }
    }
  };

  const waiting = streaming && messages.at(-1)?.text === "";

  return (
    <div className="flex min-h-0 flex-1 flex-col gap-4">
      <div className="flex min-h-0 flex-1 flex-col gap-4 overflow-y-auto pr-1">
        {messages.length === 0 ? (
          <p className="m-auto max-w-md text-center text-sm text-muted-foreground">
            Tell the assistant what you need: a server, a database, a bucket for
            files. It will ask for anything it needs and propose a request for
            you to confirm. Nothing is created until you click Confirm.
          </p>
        ) : null}
        {messages.map((message) => (
          <MessageView key={message.id} message={message} />
        ))}
        {waiting ? (
          <p className="text-sm text-muted-foreground" aria-live="polite">
            Thinking…
          </p>
        ) : null}
        {error ? (
          <p
            role="alert"
            className="rounded-md border border-destructive/40 bg-destructive/5 p-3 text-sm"
          >
            {error}
          </p>
        ) : null}
        <div ref={end} />
      </div>
      <Composer
        value={draft}
        onChange={setDraft}
        onSend={send}
        disabled={streaming}
      />
    </div>
  );
}

function MessageView({ message }: { message: ChatMessage }) {
  if (message.role === "user") {
    return (
      <div className="ml-auto max-w-[85%] rounded-lg bg-primary px-3 py-2 text-sm whitespace-pre-wrap text-primary-foreground">
        {message.text}
      </div>
    );
  }

  return (
    <div
      className={cn(
        "flex max-w-[85%] flex-col gap-3",
        !message.text && !message.proposals.length && "hidden",
      )}
    >
      {message.text ? <Markdown>{message.text}</Markdown> : null}
      {message.proposals.map((proposal) => (
        <ProposalCard key={proposal.requestId} proposal={proposal} />
      ))}
      {message.proposals.length > 1 ? (
        <ConfirmAllButton proposals={message.proposals} />
      ) : null}
    </div>
  );
}
