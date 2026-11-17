import { useTranslation } from "react-i18next";
import { api } from "@/api/client";

export function Footer() {
  const { t } = useTranslation();

  return (
    <footer className="border-t text-sm text-muted-foreground">
      <div className="mx-auto flex max-w-6xl flex-col gap-2 px-4 py-4 sm:flex-row sm:items-center sm:justify-between">
        {/* TMDB attribution (required by the TMDB terms); the TMDB logo is added with real data in I10. */}
        <p>{t("footer.tmdbAttribution")}</p>
        <ApiStatus />
      </div>
    </footer>
  );
}

/** A ping through the generated client: shows the API version from `GET /api/info`. */
function ApiStatus() {
  const { t } = useTranslation();
  const { data, isPending, isError } = api.useQuery("get", "/api/info");

  let text: string;
  if (isPending) {
    text = t("footer.apiLoading");
  } else if (isError) {
    text = t("footer.apiUnavailable");
  } else {
    text = t("footer.apiVersion", { version: data.version });
  }

  return (
    <p role="status" data-testid="api-status">
      {text}
    </p>
  );
}
