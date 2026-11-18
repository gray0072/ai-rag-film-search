import { ChevronLeft, ChevronRight } from "lucide-react";
import { useTranslation } from "react-i18next";
import { useSearchParams } from "react-router";
import { Button } from "@/components/ui/button";

interface PaginationProps {
  page: number;
  pageSize: number;
  total: number;
}

/** Previous / next links that keep the rest of the query string (filters, sort, query). */
export function Pagination({ page, pageSize, total }: PaginationProps) {
  const { t } = useTranslation();
  const [searchParams, setSearchParams] = useSearchParams();
  const pages = Math.max(1, Math.ceil(total / pageSize));

  if (pages <= 1) {
    return null;
  }

  const goTo = (next: number) => {
    const params = new URLSearchParams(searchParams);
    if (next <= 1) {
      params.delete("page");
    } else {
      params.set("page", String(next));
    }
    setSearchParams(params);
    window.scrollTo({ top: 0 });
  };

  return (
    <nav aria-label={t("pagination.label")} className="flex items-center justify-center gap-3">
      <Button
        variant="outline"
        size="sm"
        disabled={page <= 1}
        onClick={() => {
          goTo(page - 1);
        }}
      >
        <ChevronLeft aria-hidden />
        {t("pagination.previous")}
      </Button>
      <span className="text-sm text-muted-foreground">
        {t("pagination.status", { page, pages })}
      </span>
      <Button
        variant="outline"
        size="sm"
        disabled={page >= pages}
        onClick={() => {
          goTo(page + 1);
        }}
      >
        {t("pagination.next")}
        <ChevronRight aria-hidden />
      </Button>
    </nav>
  );
}
