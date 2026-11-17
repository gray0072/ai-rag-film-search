import { useTranslation } from "react-i18next";
import { PlaceholderPage } from "./PlaceholderPage";

export function CartPage() {
  const { t } = useTranslation();

  return (
    <PlaceholderPage
      title={t("pages.cart.title")}
      description={t("pages.cart.description")}
      iteration="I10"
    />
  );
}
