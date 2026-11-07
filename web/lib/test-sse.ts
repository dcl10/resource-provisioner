/**
 * A streamed `text/event-stream` response, for tests: each chunk is sent as it would arrive over the network.
 * @param chunks - the body, in pieces
 * @returns the response
 */
export function sseResponse(chunks: string[]): Response {
  const encoder = new TextEncoder();
  const body = new ReadableStream<Uint8Array>({
    start(controller) {
      for (const chunk of chunks) controller.enqueue(encoder.encode(chunk));
      controller.close();
    },
  });
  return new Response(body, {
    headers: { "Content-Type": "text/event-stream" },
  });
}

/**
 * One server-sent event, as the API writes it.
 * @param event - the event type
 * @param data - its JSON data
 * @returns the event's text
 */
export function sseEvent(event: string, data: unknown): string {
  return `event: ${event}\ndata: ${JSON.stringify(data)}\n\n`;
}
