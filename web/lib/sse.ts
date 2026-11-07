/** One server-sent event. */
export interface SseMessage {
  /** The `event:` field; `message` when there is none. */
  event: string;
  /** The `data:` lines, joined with newlines. */
  data: string;
  /** The `id:` field, if any. */
  id?: string;
}

/**
 * An incremental server-sent events parser: feed it text as it arrives, in chunks of any size, and it returns the
 * events completed so far. Follows the SSE format: events end at a blank line, lines end in `\n`, `\r\n` or `\r`,
 * lines starting with `:` are comments, and a single space after the colon is dropped.
 */
export class SseParser {
  private buffer = "";

  /**
   * Adds text and returns the events it completes.
   * @param chunk - the next piece of the stream
   * @returns the completed events, in order
   */
  push(chunk: string): SseMessage[] {
    this.buffer += chunk;
    const messages: SseMessage[] = [];

    // A trailing \r may be the first half of \r\n, so leave it for the next chunk.
    const blocks = this.buffer
      .replace(/\r\n/g, "\n")
      .replace(/\r(?!$)/g, "\n")
      .split("\n\n");
    this.buffer = blocks.pop() ?? "";
    for (const block of blocks) {
      const message = parseBlock(block);
      if (message) messages.push(message);
    }
    return messages;
  }
}

function parseBlock(block: string): SseMessage | null {
  let event = "message";
  let id: string | undefined;
  const data: string[] = [];

  for (const line of block.split("\n")) {
    if (line === "" || line.startsWith(":")) continue;
    const colon = line.indexOf(":");
    const field = colon === -1 ? line : line.slice(0, colon);
    let value = colon === -1 ? "" : line.slice(colon + 1);
    if (value.startsWith(" ")) value = value.slice(1);

    if (field === "event") event = value;
    else if (field === "data") data.push(value);
    else if (field === "id") id = value;
  }

  return data.length > 0 ? { event, data: data.join("\n"), id } : null;
}
