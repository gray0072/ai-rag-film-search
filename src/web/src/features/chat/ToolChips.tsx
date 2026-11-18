import { Check, Loader2, X } from "lucide-react";
import { useTranslation } from "react-i18next";
import { Badge } from "@/components/ui/badge";
import type { ToolCall } from "./turn";

/** Progress texts per tool (spec 06 tools); an unknown tool gets a neutral text. */
const toolKeys = {
  search_movies: "chat.tools.search_movies",
  get_movie_details: "chat.tools.get_movie_details",
  find_similar_movies: "chat.tools.find_similar_movies",
  view_cart: "chat.tools.view_cart",
  add_to_cart: "chat.tools.add_to_cart",
  remove_from_cart: "chat.tools.remove_from_cart",
  request_checkout: "chat.tools.request_checkout",
} as const;

function toolKey(name: string) {
  return Object.hasOwn(toolKeys, name)
    ? toolKeys[name as keyof typeof toolKeys]
    : "chat.tools.unknown";
}

/** What the assistant is doing right now: "Searching the catalog…" with a spinner, then a check mark. */
export function ToolChips({ tools }: { tools: ToolCall[] }) {
  const { t } = useTranslation();
  if (tools.length === 0) {
    return null;
  }

  return (
    <ul aria-label={t("chat.tools.label")} className="flex flex-wrap gap-1">
      {tools.map((tool) => (
        <li key={tool.callId}>
          <Badge variant="secondary" className="gap-1 font-normal">
            {tool.status === "running" && <Loader2 className="animate-spin" aria-hidden />}
            {tool.status === "ok" && <Check aria-hidden />}
            {tool.status === "failed" && <X aria-hidden />}
            {t(toolKey(tool.name))}
          </Badge>
        </li>
      ))}
    </ul>
  );
}
