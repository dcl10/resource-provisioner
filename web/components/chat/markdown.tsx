import ReactMarkdown from "react-markdown";
import rehypeSanitize from "rehype-sanitize";
import remarkGfm from "remark-gfm";

import { cn } from "@/lib/utils";

/**
 * Renders the assistant's Markdown, with GitHub-flavoured extensions (tables, lists, strikethrough). The output is
 * sanitized and raw HTML is never rendered: the model's text is untrusted.
 * @param children - the Markdown
 * @param className - extra classes for the wrapper
 * @returns the rendered text
 */
export function Markdown({
  children,
  className,
}: {
  children: string;
  className?: string;
}) {
  return (
    <div
      className={cn(
        "space-y-3 text-sm leading-relaxed break-words",
        "[&_a]:underline [&_a]:underline-offset-4",
        "[&_ol]:list-decimal [&_ol]:pl-5 [&_ul]:list-disc [&_ul]:pl-5 [&_li]:mt-1",
        "[&_code]:rounded [&_code]:bg-muted [&_code]:px-1 [&_code]:py-0.5 [&_code]:font-mono [&_code]:text-[0.85em]",
        "[&_pre]:overflow-x-auto [&_pre]:rounded-md [&_pre]:bg-muted [&_pre]:p-3 [&_pre_code]:bg-transparent [&_pre_code]:p-0",
        "[&_table]:w-full [&_table]:text-left [&_td]:border-t [&_td]:py-1 [&_td]:pr-3 [&_th]:py-1 [&_th]:pr-3",
        "[&_h1]:font-semibold [&_h2]:font-semibold [&_h3]:font-semibold",
        className,
      )}
    >
      <ReactMarkdown
        remarkPlugins={[remarkGfm]}
        rehypePlugins={[rehypeSanitize]}
        components={{
          a: ({ node: _node, ...props }) => (
            <a {...props} target="_blank" rel="noopener noreferrer" />
          ),
        }}
      >
        {children}
      </ReactMarkdown>
    </div>
  );
}
