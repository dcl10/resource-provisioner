import { describe, expect, it } from "vitest";

import { SseParser } from "@/lib/sse";

describe("SseParser", () => {
  it("parses complete events with their type, data and id", () => {
    const parser = new SseParser();

    const messages = parser.push(
      'event: text\ndata: {"text":"Hi"}\n\nid: 7\nevent: log\ndata: x\n\n',
    );

    expect(messages).toEqual([
      { event: "text", data: '{"text":"Hi"}', id: undefined },
      { event: "log", data: "x", id: "7" },
    ]);
  });

  it("holds a partial event until the rest arrives", () => {
    const parser = new SseParser();

    expect(parser.push("event: te")).toEqual([]);
    expect(parser.push('xt\ndata: {"te')).toEqual([]);
    expect(parser.push('xt":"a"}\n')).toEqual([]);
    expect(parser.push("\n")).toEqual([
      { event: "text", data: '{"text":"a"}', id: undefined },
    ]);
  });

  it("accepts CRLF line endings, even split between chunks", () => {
    const parser = new SseParser();

    expect(parser.push("event: done\r\ndata: {}\r")).toEqual([]);
    expect(parser.push("\n\r\n")).toEqual([
      { event: "done", data: "{}", id: undefined },
    ]);
  });

  it("joins multiple data lines and defaults the type to message", () => {
    const parser = new SseParser();

    expect(parser.push("data: one\ndata:two\n\n")).toEqual([
      { event: "message", data: "one\ntwo", id: undefined },
    ]);
  });

  it("ignores comments and events without data", () => {
    const parser = new SseParser();

    expect(parser.push(": keep-alive\n\nevent: ping\n\n")).toEqual([]);
  });
});
