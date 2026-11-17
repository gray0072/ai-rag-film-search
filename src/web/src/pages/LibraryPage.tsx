import { useTranslation } from "react-i18next";
import { PlaceholderPage } from "./PlaceholderPage";

export function LibraryPage() {
  const { t } = useTranslation();

  return (
    <PlaceholderPage
      title={t("pages.library.title")}
      description={t("pages.library.description")}
      iteration="I10"
    />
  );
}
