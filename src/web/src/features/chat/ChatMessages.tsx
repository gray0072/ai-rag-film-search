import { Bot } from "lucide-react";
import type { ReactNode } from "react";
import { useTranslation } from "react-i18next";
import type { AssistantMovie, ConfirmationView, MessageView } from "@/api/types";
import { ChatMovieCard } from "./ChatMovieCard";
import { ConfirmationCard } from "./ConfirmationCard";
import { FeedbackButtons } from "./FeedbackButtons";
import { Markdown } from "./Markdown";
import { ToolChips } from "./ToolChips";
import type { ToolCall } from "./turn";

export function UserBubble({ text }: { text: string }) {
  const { t } = useTranslation();
  return (
    <li className="flex justify-end" aria-label={t("chat.you")}>
      <p className="max-w-[85%] rounded-2xl rounded-br-sm bg-primary px-3 py-2 text-sm whitespace-pre-wrap text-primary-foreground">
        {text}
      </p>
    </li>
  );
}

interface AssistantBubbleProps {
  conversationId: string;
  text: string;
  tools?: ToolCall[];
  movies: AssistantMovie[];
  confirmation?: ConfirmationView | null;
  /** Typing indicator, stop note, error or feedback. */
  footer?: ReactNode;
}

export function AssistantBubble({
  conversationId,
  text,
  tools = [],
  movies,
  confirmation,
  footer,
}: AssistantBubbleProps) {
  const { t } = useTranslation();
  return (
    <li className="flex gap-2" aria-label={t("chat.assistant")}>
      <span className="mt-1 flex size-7 shrink-0 items-center justify-center rounded-full bg-muted">
        <Bot className="size-4" aria-hidden />
      </span>
      <div className="flex min-w-0 flex-1 flex-col gap-2">
        <ToolChips tools={tools} />
        {text && <Markdown text={text} />}
        {movies.length > 0 && (
          <div
            role="group"
            aria-label={t("chat.movies")}
            className="-mx-1 flex gap-2 overflow-x-auto px-1 pb-1"
          >
            {movies.map((movie) => (
              <ChatMovieCard key={movie.id} movie={movie} />
            ))}
          </div>
        )}
        {confirmation && (
          <ConfirmationCard conversationId={conversationId} confirmation={confirmation} />
        )}
        {footer}
      </div>
    </li>
  );
}

/** A stored message of the conversation (after a reload: FR-AST-2). */
export function HistoryMessage({
  conversationId,
  message,
}: {
  conversationId: string;
  message: MessageView;
}) {
  if (message.role === "user") {
    return <UserBubble text={message.text} />;
  }
  return (
    <AssistantBubble
      conversationId={conversationId}
      text={message.text}
      movies={message.movies}
      confirmation={message.confirmation}
      footer={<FeedbackButtons messageId={message.id} rating={message.feedback?.rating} />}
    />
  );
}

export function TypingIndicator() {
  const { t } = useTranslation();
  return (
    <p role="status" className="flex items-center gap-1 text-sm text-muted-foreground">
      <span className="flex gap-0.5" aria-hidden>
        <span className="size-1.5 animate-bounce rounded-full bg-current [animation-delay:-0.3s]" />
        <span className="size-1.5 animate-bounce rounded-full bg-current [animation-delay:-0.15s]" />
        <span className="size-1.5 animate-bounce rounded-full bg-current" />
      </span>
      {t("chat.typing")}
    </p>
  );
}
