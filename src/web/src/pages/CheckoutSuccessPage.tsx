import { useTranslation } from "react-i18next";
import { PlaceholderPage } from "./PlaceholderPage";

export function CheckoutSuccessPage() {
  const { t } = useTranslation();

  return (
    <PlaceholderPage
      title={t("pages.checkoutSuccess.title")}
      description={t("pages.checkoutSuccess.description")}
      iteration="I10"
    />
  );
}
