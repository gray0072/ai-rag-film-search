import { Plus } from "lucide-react";
import { useTranslation } from "react-i18next";
import { Link } from "react-router";
import { api } from "@/api/client";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import { currentLanguage } from "@/i18n";
import { formatDateTime } from "@/lib/format";
import { cn } from "@/lib/utils";

/** FR-AST-2: the owner's conversations, most recently updated first, and a new one. */
export function ConversationList({ activeId }: { activeId: string | undefined }) {
  const { t } = useTranslation();
  const language = currentLanguage();
  const conversations = api.useQuery("get", "/api/chat/conversations");

  return (
    <nav aria-label={t("chat.conversations")} className="flex flex-col gap-2">
      <Button asChild variant="outline" size="sm" className="justify-start">
        <Link to="/chat">
          <Plus aria-hidden />
          {t("chat.newConversation")}
        </Link>
      </Button>
      {conversations.isPending ? (
        <div role="status" aria-label={t("common.loading")} className="flex flex-col gap-2">
          <Skeleton className="h-10 w-full" />
          <Skeleton className="h-10 w-full" />
        </div>
      ) : conversations.isError ? (
        <p className="text-sm text-destructive">{t("common.errorGeneric")}</p>
      ) : (
        <ul className="flex flex-col gap-1">
          {conversations.data.map((conversation) => (
            <li key={conversation.id}>
              <Link
                to={`/chat/${conversation.id}`}
                aria-current={conversation.id === activeId ? "page" : undefined}
                className={cn(
                  "flex flex-col rounded-md px-2 py-1.5 text-sm hover:bg-accent",
                  conversation.id === activeId && "bg-accent",
                )}
              >
                <span className="truncate">{conversation.title ?? t("chat.untitled")}</span>
                <span className="text-xs text-muted-foreground">
                  {formatDateTime(conversation.updatedAt, language)}
                </span>
              </Link>
            </li>
          ))}
        </ul>
      )}
    </nav>
  );
}
