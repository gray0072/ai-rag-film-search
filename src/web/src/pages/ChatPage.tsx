import { useTranslation } from "react-i18next";
import { useNavigate, useParams } from "react-router";
import { PageTitle } from "@/components/StatusMessages";
import { ChatPanel } from "@/features/chat/ChatPanel";
import { ConversationList } from "@/features/chat/ConversationList";

/** `/chat` (a new conversation) and `/chat/:id`: the assistant with the conversation list. */
export function ChatPage() {
  const { t } = useTranslation();
  const { id } = useParams();
  const navigate = useNavigate();

  return (
    <section className="flex flex-col gap-4">
      <div className="flex flex-col gap-1">
        <PageTitle>{t("chat.title")}</PageTitle>
        <p className="text-sm text-muted-foreground">{t("chat.description")}</p>
      </div>
      <div className="grid gap-6 md:grid-cols-[14rem_1fr]">
        <ConversationList activeId={id} />
        <ChatPanel
          conversationId={id}
          onConversationCreated={(created) => {
            void navigate(`/chat/${created}`, { replace: true });
          }}
          className="h-[70svh] min-w-0 rounded-md border p-4"
        />
      </div>
    </section>
  );
}
