import { useMutation } from "@tanstack/react-query";
import { ThumbsDown, ThumbsUp } from "lucide-react";
import { useState } from "react";
import { useTranslation } from "react-i18next";
import { apiClient } from "@/api/client";
import { ApiError } from "@/api/problem";
import { Button } from "@/components/ui/button";
import { cn } from "@/lib/utils";

interface FeedbackButtonsProps {
  messageId: string;
  /** The stored rating: 1, -1 or none. */
  rating: number | undefined;
}

/** FR-AST-9: thumbs up / down on an answer; a new rating replaces the old one. */
export function FeedbackButtons({ messageId, rating }: FeedbackButtonsProps) {
  const { t } = useTranslation();
  const [current, setCurrent] = useState(rating);
  const save = useMutation({
    mutationFn: async (value: 1 | -1) => {
      const { error } = await apiClient.POST("/api/chat/messages/{messageId}/feedback", {
        params: { path: { messageId } },
        body: { rating: value },
      });
      if (error !== undefined) {
        throw new ApiError(error);
      }
      return value;
    },
    onSuccess: setCurrent,
  });

  const button = (value: 1 | -1) => {
    const Icon = value === 1 ? ThumbsUp : ThumbsDown;
    const pressed = current === value;
    return (
      <Button
        variant="ghost"
        size="icon"
        className="size-7"
        aria-label={value === 1 ? t("chat.feedback.up") : t("chat.feedback.down")}
        aria-pressed={pressed}
        disabled={save.isPending}
        onClick={() => {
          save.mutate(value);
        }}
      >
        <Icon className={cn("size-3.5", pressed && "fill-current")} aria-hidden />
      </Button>
    );
  };

  return (
    <div className="flex items-center gap-1 text-xs text-muted-foreground">
      {button(1)}
      {button(-1)}
      {save.isSuccess && <span role="status">{t("chat.feedback.thanks")}</span>}
      {save.isError && (
        <span role="alert" className="text-destructive">
          {t("common.errorGeneric")}
        </span>
      )}
    </div>
  );
}
