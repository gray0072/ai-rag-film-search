import { Maximize2, MessageCircle, Plus } from "lucide-react";
import { useState } from "react";
import { useTranslation } from "react-i18next";
import { Link } from "react-router";
import { Button } from "@/components/ui/button";
import {
  Sheet,
  SheetContent,
  SheetDescription,
  SheetHeader,
  SheetTitle,
  SheetTrigger,
} from "@/components/ui/sheet";
import { ChatPanel } from "./ChatPanel";

/**
 * The assistant in a side drawer, available from every page (the header button). It keeps its conversation
 * while the user browses; the full page has the conversation list.
 */
export function ChatDrawer() {
  const { t } = useTranslation();
  const [open, setOpen] = useState(false);
  const [conversationId, setConversationId] = useState<string>();

  return (
    <Sheet open={open} onOpenChange={setOpen}>
      <SheetTrigger asChild>
        <Button variant="ghost" size="sm">
          <MessageCircle aria-hidden />
          <span className="sr-only sm:not-sr-only">{t("nav.chat")}</span>
        </Button>
      </SheetTrigger>
      <SheetContent className="w-full gap-0 sm:max-w-md">
        <SheetHeader className="flex-row items-center gap-1 border-b pr-12">
          <div className="flex min-w-0 flex-1 flex-col gap-0.5">
            <SheetTitle>{t("chat.title")}</SheetTitle>
            <SheetDescription className="sr-only">{t("chat.description")}</SheetDescription>
          </div>
          <Button
            variant="ghost"
            size="icon"
            aria-label={t("chat.newConversation")}
            onClick={() => {
              setConversationId(undefined);
            }}
          >
            <Plus aria-hidden />
          </Button>
          <Button asChild variant="ghost" size="icon">
            <Link
              to={conversationId ? `/chat/${conversationId}` : "/chat"}
              aria-label={t("chat.openFullPage")}
              onClick={() => {
                setOpen(false);
              }}
            >
              <Maximize2 aria-hidden />
            </Link>
          </Button>
        </SheetHeader>
        <ChatPanel
          conversationId={conversationId}
          onConversationCreated={setConversationId}
          className="flex-1 p-4"
        />
      </SheetContent>
    </Sheet>
  );
}
