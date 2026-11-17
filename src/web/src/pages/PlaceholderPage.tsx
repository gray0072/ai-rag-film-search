import type { ReactNode } from "react";
import { useTranslation } from "react-i18next";

interface PlaceholderPageProps {
  title: string;
  description: string;
  /** The iteration that builds the real page, e.g. `I10`. */
  iteration: string;
  children?: ReactNode;
}

export function PlaceholderPage({ title, description, iteration, children }: PlaceholderPageProps) {
  const { t } = useTranslation();

  return (
    <section className="space-y-3">
      <h1 className="text-2xl font-semibold tracking-tight">{title}</h1>
      <p className="text-muted-foreground">{description}</p>
      {children}
      <p className="rounded-md border border-dashed p-4 text-sm text-muted-foreground">
        {t("placeholder.comingIn", { iteration })}
      </p>
    </section>
  );
}
