import { MessageSquare } from "lucide-react";

import { NewConversationButton } from "@/components/chat/conversation-list";

export default function ChatPage() {
  return (
    <div className="m-auto flex max-w-md flex-col items-center gap-4 text-center">
      <MessageSquare className="size-8 text-muted-foreground" />
      <h1 className="text-2xl font-semibold tracking-tight">
        What do you need?
      </h1>
      <p className="text-sm text-muted-foreground">
        Chat with the assistant to provision a server, a database or a bucket
        from the vetted catalog. It proposes a request; nothing is created until
        you confirm it.
      </p>
      <NewConversationButton />
    </div>
  );
}
