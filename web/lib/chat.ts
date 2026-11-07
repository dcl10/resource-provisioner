import { API_BASE, ApiError, apiFetch, readApiError } from "@/lib/api";
import { SseParser } from "@/lib/sse";

/** The longest message the API accepts, in characters. */
export const MAX_MESSAGE_LENGTH = 4000;

/** A conversation in the list. Dates are ISO 8601. */
export interface ConversationSummary {
  id: string;
  ownerId: string;
  ownerEmail: string | null;
  /** The start of its first message; null until one is sent. */
  title: string | null;
  createdAt: string;
  updatedAt: string;
}

/** One variable on a confirmation card. */
export interface ProposalItem {
  name: string;
  label: string;
  value: string;
}

/** A request the agent proposed, from the `proposal` event or a conversation's history. */
export interface Proposal {
  requestId: string;
  type: string;
  displayName: string;
  costHint: string;
  items: ProposalItem[];
}

/** A message as the chat shows it. */
export interface ChatMessage {
  id: string;
  role: "user" | "assistant";
  /** Plain text for the user; Markdown for the assistant. */
  text: string;
  proposals: Proposal[];
}

/** A conversation with its messages. */
export interface Conversation extends ConversationSummary {
  messages: (Omit<ChatMessage, "id"> & { id: number; createdAt: string })[];
}

/** Callbacks for the events of a streamed reply. */
export interface ReplyHandlers {
  /** A piece of the reply's text. */
  onText: (text: string) => void;
  /** A request the agent proposed. */
  onProposal: (proposal: Proposal) => void;
}

/**
 * Lists the user's conversations, most recently active first.
 * @returns the conversations
 */
export function listConversations(): Promise<ConversationSummary[]> {
  return apiFetch<ConversationSummary[]>("/conversations");
}

/**
 * Reads a conversation with its messages.
 * @param id - the conversation ID
 * @returns the conversation
 */
export function getConversation(id: string): Promise<Conversation> {
  return apiFetch<Conversation>(`/conversations/${encodeURIComponent(id)}`);
}

/**
 * Starts an empty conversation.
 * @returns the new conversation
 */
export function createConversation(): Promise<ConversationSummary> {
  return apiFetch<ConversationSummary>("/conversations", { method: "POST" });
}

/**
 * Sends a message and streams the agent's reply, calling the handlers as events arrive. Resolves when the reply is
 * done.
 * @param conversationId - the conversation
 * @param text - what the user typed
 * @param handlers - called for each text delta and proposal
 * @param signal - aborts the reply
 * @throws ApiError when the message is refused (429 over the rate limit, 403, 404, 400) or the reply ends in an
 * `error` event (status 0)
 */
export async function sendMessage(
  conversationId: string,
  text: string,
  handlers: ReplyHandlers,
  signal?: AbortSignal,
): Promise<void> {
  const response = await fetch(
    `${API_BASE}/conversations/${encodeURIComponent(conversationId)}/messages`,
    {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        Accept: "text/event-stream",
      },
      body: JSON.stringify({ text }),
      signal,
    },
  );
  if (!response.ok) {
    const error = await readApiError(response);
    if (response.status === 429) {
      error.message =
        "You're sending messages faster than the app allows. Wait a minute, then try again.";
    }
    throw error;
  }
  if (!response.body) {
    throw new ApiError("The reply couldn't be read. Try again.", 0);
  }

  const parser = new SseParser();
  const reader = response.body.pipeThrough(new TextDecoderStream()).getReader();
  for (;;) {
    const { value, done } = await reader.read();
    if (done) break;
    for (const message of parser.push(value)) {
      const data = JSON.parse(message.data) as {
        text?: string;
        error?: string;
      };
      switch (message.event) {
        case "text":
          handlers.onText(data.text ?? "");
          break;
        case "proposal":
          handlers.onProposal(data as unknown as Proposal);
          break;
        case "error":
          throw new ApiError(data.error ?? "The reply stopped early.", 0);
        case "done":
          return;
      }
    }
  }

  throw new ApiError("The reply ended unexpectedly. Try again.", 0);
}
