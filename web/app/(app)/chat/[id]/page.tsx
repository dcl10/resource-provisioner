import { Suspense } from "react";

import {
  ChatConversation,
  ChatSkeleton,
} from "@/components/chat/chat-conversation";

export default function ConversationPage({ params }: PageProps<"/chat/[id]">) {
  return (
    <Suspense fallback={<ChatSkeleton />}>
      {params.then(({ id }) => (
        <ChatConversation key={id} conversationId={id} />
      ))}
    </Suspense>
  );
}
