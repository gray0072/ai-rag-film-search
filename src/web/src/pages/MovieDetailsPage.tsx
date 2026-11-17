import { useTranslation } from "react-i18next";
import { useParams } from "react-router";
import { PlaceholderPage } from "./PlaceholderPage";

export function MovieDetailsPage() {
  const { t } = useTranslation();
  const { id = "" } = useParams();

  return (
    <PlaceholderPage
      title={t("pages.movie.title")}
      description={t("pages.movie.description", { id })}
      iteration="I10"
    />
  );
}
