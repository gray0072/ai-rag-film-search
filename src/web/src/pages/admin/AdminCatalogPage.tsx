import { CheckCircle2, Info } from "lucide-react";
import { useId, useState, type SubmitEvent } from "react";
import { useTranslation } from "react-i18next";
import { Link } from "react-router";
import { api } from "@/api/client";
import { ErrorState } from "@/components/StatusMessages";
import { Alert, AlertDescription } from "@/components/ui/alert";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
import { conflictingJobId, parseTmdbIds, useRefreshAdmin } from "@/features/admin/admin";
import { AdminSection } from "@/features/admin/AdminLayout";
import { SelectField } from "@/features/filters/FiltersPanel";

/** Catalog content (I22): load the seed file, queue a TMDB import. */
export function AdminCatalogPage() {
  const { t } = useTranslation();
  return (
    <div className="flex flex-col gap-4">
      <p className="text-sm text-muted-foreground">{t("admin.catalog.description")}</p>
      <div className="grid gap-4 lg:grid-cols-2">
        <SeedSection />
        <TmdbImportSection />
      </div>
    </div>
  );
}

function SeedSection() {
  const { t, i18n } = useTranslation();
  const refresh = useRefreshAdmin();
  const seed = api.useMutation("post", "/api/admin/ingestion/seed", {
    onSuccess: () => void refresh(),
  });
  const seconds = (milliseconds: number) =>
    new Intl.NumberFormat(i18n.language, { maximumFractionDigits: 1 }).format(milliseconds / 1000);

  return (
    <AdminSection
      title={t("admin.catalog.seed.title")}
      description={t("admin.catalog.seed.description")}
    >
      <div>
        <Button
          disabled={seed.isPending}
          onClick={() => {
            seed.mutate({});
          }}
        >
          {seed.isPending ? t("admin.catalog.seed.running") : t("admin.catalog.seed.submit")}
        </Button>
      </div>
      {seed.isError && <ErrorState />}
      {seed.data && (
        <Alert>
          <CheckCircle2 aria-hidden />
          <AlertDescription>
            <p>
              {t("admin.catalog.seed.result", {
                genres: seed.data.genres,
                created: seed.data.created,
                updated: seed.data.updated,
                unchanged: seed.data.unchanged,
                seconds: seconds(seed.data.elapsedMilliseconds),
              })}
            </p>
            <p>{t("admin.catalog.seed.reindexHint")}</p>
          </AlertDescription>
        </Alert>
      )}
    </AdminSection>
  );
}

type Source = "list" | "ids";

function TmdbImportSection() {
  const { t } = useTranslation();
  const id = useId();
  const refresh = useRefreshAdmin();
  const [source, setSource] = useState<Source>("list");
  const [list, setList] = useState<"topRated" | "popular">("popular");
  const [pages, setPages] = useState(5);
  const [idsText, setIdsText] = useState("");
  const [idsInvalid, setIdsInvalid] = useState(false);
  const queue = api.useMutation("post", "/api/admin/ingestion/tmdb", {
    onSettled: () => void refresh(),
  });

  const onSubmit = (event: SubmitEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (source === "list") {
      queue.mutate({ body: { list, pages } });
      return;
    }
    const tmdbIds = parseTmdbIds(idsText);
    setIdsInvalid(tmdbIds === undefined);
    if (tmdbIds) {
      queue.mutate({ body: { tmdbIds } });
    }
  };

  const conflictId = queue.isError ? conflictingJobId(queue.error) : undefined;

  return (
    <AdminSection
      title={t("admin.catalog.import.title")}
      description={t("admin.catalog.import.description")}
    >
      <form onSubmit={onSubmit} className="flex flex-col gap-3">
        <fieldset className="flex flex-col gap-2">
          <legend className="mb-1 text-sm font-medium">{t("admin.catalog.import.source")}</legend>
          {(["list", "ids"] as const).map((value) => (
            <div key={value} className="flex items-center gap-2 text-sm">
              <input
                type="radio"
                id={`${id}-${value}`}
                name={`${id}-source`}
                checked={source === value}
                onChange={() => {
                  setSource(value);
                }}
                className="size-4 accent-primary"
              />
              <Label htmlFor={`${id}-${value}`} className="font-normal">
                {value === "list"
                  ? t("admin.catalog.import.fromList")
                  : t("admin.catalog.import.fromIds")}
              </Label>
            </div>
          ))}
        </fieldset>

        {source === "list" ? (
          <div className="grid grid-cols-2 gap-3">
            <SelectField
              label={t("admin.catalog.import.list")}
              value={list}
              onChange={(value) => {
                setList(value === "topRated" ? "topRated" : "popular");
              }}
            >
              <option value="popular">{t("admin.catalog.import.popular")}</option>
              <option value="topRated">{t("admin.catalog.import.topRated")}</option>
            </SelectField>
            <div className="flex flex-col gap-1">
              <Label htmlFor={`${id}-pages`} className="font-medium">
                {t("admin.catalog.import.pages")}
              </Label>
              <Input
                id={`${id}-pages`}
                type="number"
                min={1}
                max={500}
                required
                value={pages}
                onChange={(event) => {
                  setPages(Number(event.target.value));
                }}
              />
            </div>
          </div>
        ) : (
          <div className="flex flex-col gap-1">
            <Label htmlFor={`${id}-tmdb-ids`} className="font-medium">
              {t("admin.catalog.import.ids")}
            </Label>
            <Textarea
              id={`${id}-tmdb-ids`}
              value={idsText}
              rows={3}
              aria-invalid={idsInvalid}
              aria-describedby={`${id}-tmdb-ids-hint`}
              onChange={(event) => {
                setIdsText(event.target.value);
              }}
            />
            <p id={`${id}-tmdb-ids-hint`} className="text-xs text-muted-foreground">
              {idsInvalid
                ? t("admin.catalog.import.invalidIds")
                : t("admin.catalog.import.idsHint")}
            </p>
          </div>
        )}

        <div>
          <Button type="submit" disabled={queue.isPending}>
            {t("admin.catalog.import.submit")}
          </Button>
        </div>
      </form>

      {queue.data && (
        <Alert>
          <CheckCircle2 aria-hidden />
          <AlertDescription>
            <p>
              {t("admin.catalog.import.queued")}{" "}
              <Link to={`/admin/jobs/${queue.data.id}`} className="underline underline-offset-4">
                {t("admin.catalog.import.openJob")}
              </Link>
            </p>
          </AlertDescription>
        </Alert>
      )}
      {conflictId && (
        <Alert>
          <Info aria-hidden />
          <AlertDescription>
            <p>
              {t("admin.catalog.import.conflict")}{" "}
              <Link to={`/admin/jobs/${conflictId}`} className="underline underline-offset-4">
                {t("admin.catalog.import.openJob")}
              </Link>
            </p>
          </AlertDescription>
        </Alert>
      )}
      {queue.isError && !conflictId && <ErrorState />}
    </AdminSection>
  );
}
