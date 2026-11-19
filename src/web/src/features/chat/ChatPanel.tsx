import { useQueryClient } from "@tanstack/react-query";
import { SendHorizontal, Square } from "lucide-react";
import { useEffect, useRef, useState, type KeyboardEvent, type SubmitEvent } from "react";
import { useTranslation } from "react-i18next";
import { api } from "@/api/client";
import { problemCode } from "@/api/problem";
import { ErrorState } from "@/components/StatusMessages";
import { Alert, AlertDescription } from "@/components/ui/alert";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import { Textarea } from "@/components/ui/textarea";
import { cartQueryKey } from "@/features/cart/cart";
import { currentLanguage } from "@/i18n";
import { cn } from "@/lib/utils";
import {
  conversationQueryKey,
  conversationsQueryKey,
  createConversation,
  streamAnswer,
} from "./chatApi";
import { AssistantBubble, HistoryMessage, TypingIndicator, UserBubble } from "./ChatMessages";
import { FeedbackButtons } from "./FeedbackButtons";
import { applyEvent, startTurn, type Turn } from "./turn";

/** Spec 04: 1–2000 characters after trim. */
const maxMessageLength = 2000;

/** Error codes of the stream (spec 04) and of the client that have their own message. */
const errorKeys = {
  providerUnavailable: "chat.errors.providerUnavailable",
  conversationBusy: "chat.errors.conversationBusy",
  budgetExceeded: "chat.errors.budgetExceeded",
  dailyBudgetExceeded: "chat.errors.dailyBudgetExceeded",
  serviceBudgetExceeded: "chat.errors.serviceBudgetExceeded",
  toolsNotSupported: "chat.errors.toolsNotSupported",
  internalError: "chat.errors.internalError",
  connectionLost: "chat.errors.connectionLost",
  rateLimited: "chat.errors.rateLimited",
} as const;

function errorKey(code: string | undefined) {
  return code !== undefined && Object.hasOwn(errorKeys, code)
    ? errorKeys[code as keyof typeof errorKeys]
    : "common.errorGeneric";
}

const examples = [
  "chat.examples.evening",
  "chat.examples.timeTravel",
  "chat.examples.cart",
] as const;

type KeyedTurn = Turn & { key: number };

interface ChatPanelProps {
  /** The open conversation; none: a new one is created with the first message. */
  conversationId: string | undefined;
  onConversationCreated: (id: string) => void;
  className?: string;
}

/**
 * A conversation with the assistant: history, the streamed answer (FR-AST-1), tool progress, movie cards,
 * the checkout confirmation and feedback. Leaving the page does not cancel the answer (the server finishes
 * and stores it); only Stop does.
 */
export function ChatPanel({ conversationId, onConversationCreated, className }: ChatPanelProps) {
  const { t } = useTranslation();
  const queryClient = useQueryClient();
  const [input, setInput] = useState("");
  const [turn, setTurn] = useState<KeyedTurn>();
  const abort = useRef<AbortController>(undefined);
  const nextKey = useRef(0);
  const end = useRef<HTMLDivElement>(null);

  const conversation = api.useQuery(
    "get",
    "/api/chat/conversations/{id}",
    { params: { path: { id: conversationId ?? "" } } },
    { enabled: conversationId !== undefined },
  );

  // A new conversation: the page switches to the created id a little later (the route change is a transition).
  // Until then the turn stays on the "new conversation" view, then it moves with the page.
  if (
    turn?.createdId !== undefined &&
    turn.createdId === conversationId &&
    turn.conversationId === ""
  ) {
    setTurn({ ...turn, conversationId: turn.createdId });
  }
  const visibleTurn = turn?.conversationId === (conversationId ?? "") ? turn : undefined;
  const streaming = visibleTurn?.status === "streaming";
  const history = conversation.data?.messages ?? [];
  // Until the turn settles, messages stored meanwhile (its own user message) are shown by the turn itself.
  const shownHistory =
    visibleTurn && !visibleTurn.settled ? history.slice(0, visibleTurn.historyLength) : history;

  useEffect(() => {
    end.current?.scrollIntoView({ block: "end" });
  }, [shownHistory.length, visibleTurn]);

  const send = async (raw: string) => {
    const text = raw.trim();
    if (text === "" || streaming) {
      return;
    }
    const key = ++nextKey.current;
    const controller = new AbortController();
    abort.current = controller;
    // Events of an older turn (another conversation was opened meanwhile) never touch a newer one.
    const update = (change: (current: Turn) => Turn | undefined) => {
      setTurn((current) => {
        if (current?.key !== key) {
          return current;
        }
        const next = change(current);
        return next && { ...next, key };
      });
    };

    setInput("");
    setTurn({ ...startTurn(conversationId ?? "", text, history.length), key });

    let id = conversationId;
    // Set inside the event callback, so an object (a plain boolean would be narrowed to `false`).
    const progress = { userStored: false };
    try {
      if (id === undefined) {
        const created = await createConversation(currentLanguage());
        id = created;
        update((current) => ({ ...current, createdId: created }));
        onConversationCreated(created);
      }
      await streamAnswer(id, text, controller.signal, (event) => {
        if (event.type === "message.started") {
          progress.userStored = true;
        }
        if (event.type === "cart.updated") {
          queryClient.setQueryData(cartQueryKey, event.cart);
        }
        update((current) => applyEvent(current, event));
      });
      // The stream ended without message.completed or error: the connection broke.
      update((current) =>
        current.status === "streaming"
          ? { ...current, status: "failed", error: "connectionLost" }
          : current,
      );
    } catch (error) {
      update((current) =>
        controller.signal.aborted
          ? { ...current, status: "stopped" }
          : { ...current, status: "failed", error: problemCode(error) },
      );
    } finally {
      if (id !== undefined) {
        await Promise.all([
          queryClient.invalidateQueries({ queryKey: conversationQueryKey(id) }),
          queryClient.invalidateQueries({ queryKey: conversationsQueryKey }),
        ]);
      }
      // A completed answer is in the reloaded history now; a stopped or failed one stays on screen.
      update((current) =>
        current.status === "completed" ? undefined : { ...current, settled: true },
      );
      if (!progress.userStored) {
        // The server did not store the message: give it back for another try.
        setInput((current) => current || text);
      }
    }
  };

  const onSubmit = (event: SubmitEvent<HTMLFormElement>) => {
    event.preventDefault();
    void send(input);
  };

  const onKeyDown = (event: KeyboardEvent<HTMLTextAreaElement>) => {
    if (event.key === "Enter" && !event.shiftKey && !event.nativeEvent.isComposing) {
      event.preventDefault();
      void send(input);
    }
  };

  const loading = conversationId !== undefined && conversation.isPending && !visibleTurn;
  const empty = shownHistory.length === 0 && !visibleTurn;
  const showAnswer =
    visibleTurn !== undefined &&
    (!visibleTurn.settled || visibleTurn.text !== "" || visibleTurn.error !== undefined);

  return (
    <div className={cn("flex min-h-0 flex-col gap-3", className)}>
      <div className="min-h-0 flex-1 overflow-y-auto pr-1">
        {loading ? (
          <div role="status" aria-label={t("common.loading")} className="flex flex-col gap-3">
            <Skeleton className="ml-auto h-10 w-2/3" />
            <Skeleton className="h-24 w-full" />
          </div>
        ) : conversation.isError ? (
          <ErrorState onRetry={() => void conversation.refetch()} />
        ) : empty ? (
          <div className="flex flex-col gap-3 py-4 text-sm text-muted-foreground">
            <p>{t("chat.intro")}</p>
            <ul aria-label={t("chat.examples.label")} className="flex flex-col items-start gap-2">
              {examples.map((example) => (
                <li key={example}>
                  <Button
                    variant="outline"
                    size="sm"
                    className="h-auto py-1.5 text-left whitespace-normal"
                    onClick={() => void send(t(example))}
                  >
                    {t(example)}
                  </Button>
                </li>
              ))}
            </ul>
          </div>
        ) : (
          <ol aria-label={t("chat.messages")} className="flex flex-col gap-4">
            {shownHistory.map((message) => (
              <HistoryMessage
                key={message.id}
                conversationId={conversationId ?? ""}
                message={message}
              />
            ))}
            {visibleTurn && !visibleTurn.settled && <UserBubble text={visibleTurn.userText} />}
            {visibleTurn && showAnswer && (
              <AssistantBubble
                conversationId={visibleTurn.createdId ?? visibleTurn.conversationId}
                text={visibleTurn.text}
                tools={visibleTurn.tools}
                movies={visibleTurn.movies}
                confirmation={visibleTurn.confirmation}
                footer={<TurnFooter turn={visibleTurn} />}
              />
            )}
          </ol>
        )}
        <div ref={end} />
      </div>

      <form onSubmit={onSubmit} className="flex items-end gap-2">
        <Textarea
          value={input}
          onChange={(event) => {
            setInput(event.target.value);
          }}
          onKeyDown={onKeyDown}
          maxLength={maxMessageLength}
          rows={2}
          placeholder={t("chat.placeholder")}
          aria-label={t("chat.inputLabel")}
          className="max-h-40 min-h-10 resize-none"
        />
        {streaming ? (
          <Button
            type="button"
            variant="outline"
            size="icon"
            aria-label={t("chat.stop")}
            onClick={() => {
              abort.current?.abort();
            }}
          >
            <Square className="fill-current" aria-hidden />
          </Button>
        ) : (
          <Button
            type="submit"
            size="icon"
            aria-label={t("chat.send")}
            disabled={input.trim() === ""}
          >
            <SendHorizontal aria-hidden />
          </Button>
        )}
      </form>
    </div>
  );
}

function TurnFooter({ turn }: { turn: Turn }) {
  const { t } = useTranslation();
  const running = turn.tools.some((tool) => tool.status === "running");

  switch (turn.status) {
    case "streaming":
      return running || turn.text !== "" ? null : <TypingIndicator />;
    case "completed":
      return turn.messageId ? (
        <FeedbackButtons messageId={turn.messageId} rating={undefined} />
      ) : null;
    case "stopped":
      return <p className="text-xs text-muted-foreground">{t("chat.stopped")}</p>;
    case "failed":
      return (
        <Alert variant="destructive">
          <AlertDescription>{t(errorKey(turn.error))}</AlertDescription>
        </Alert>
      );
  }
}
