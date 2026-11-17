import { useTranslation } from "react-i18next";
import { PlaceholderPage } from "./PlaceholderPage";

export function CatalogPage() {
  const { t } = useTranslation();

  return (
    <PlaceholderPage
      title={t("pages.catalog.title")}
      description={t("pages.catalog.description")}
      iteration="I10"
    />
  );
}
