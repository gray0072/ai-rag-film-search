import { useTranslation } from "react-i18next";
import { useSearchParams } from "react-router";
import { PlaceholderPage } from "./PlaceholderPage";

export function SearchPage() {
  const { t } = useTranslation();
  const [searchParams] = useSearchParams();
  const query = searchParams.get("q");

  return (
    <PlaceholderPage
      title={t("pages.search.title")}
      description={t("pages.search.description")}
      iteration="I10"
    >
      {query && <p className="font-medium">{t("pages.search.query", { query })}</p>}
    </PlaceholderPage>
  );
}
