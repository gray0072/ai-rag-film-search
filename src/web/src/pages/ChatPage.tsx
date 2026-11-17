import { useTranslation } from "react-i18next";
import { PlaceholderPage } from "./PlaceholderPage";

export function ChatPage() {
  const { t } = useTranslation();

  return (
    <PlaceholderPage
      title={t("pages.chat.title")}
      description={t("pages.chat.description")}
      iteration="I13"
    />
  );
}
