import { useTranslation } from "react-i18next";
import { Link } from "react-router";
import { Button } from "@/components/ui/button";

export function NotFoundPage() {
  const { t } = useTranslation();

  return (
    <section className="space-y-3">
      <h1 className="text-2xl font-semibold tracking-tight">{t("pages.notFound.title")}</h1>
      <p className="text-muted-foreground">{t("pages.notFound.description")}</p>
      <Button asChild variant="outline">
        <Link to="/">{t("pages.notFound.backHome")}</Link>
      </Button>
    </section>
  );
}
