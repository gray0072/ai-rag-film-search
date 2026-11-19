import { CheckCircle2, Info, Trash2 } from "lucide-react";
import { useId, useState, type SubmitEvent } from "react";
import { useTranslation } from "react-i18next";
import { Link } from "react-router";
import { api } from "@/api/client";
import type { components } from "@/api/schema";
import { EmptyState, ErrorState } from "@/components/StatusMessages";
import { Alert, AlertDescription } from "@/components/ui/alert";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Checkbox } from "@/components/ui/checkbox";
import { Label } from "@/components/ui/label";
import { Skeleton } from "@/components/ui/skeleton";
import {
  activeJobPollMs,
  conflictingJobId,
  isActive,
  jobSummary,
  useRefreshAdmin,
} from "@/features/admin/admin";
import { AdminSection, JobStateBadge } from "@/features/admin/AdminLayout";
import { SelectField } from "@/features/filters/FiltersPanel";
import { supportedLanguages } from "@/i18n/languages";

type EmbeddingIndexState = components["schemas"]["EmbeddingIndexState"];
type EmbeddingSetStats = components["schemas"]["EmbeddingSetStats"];

/** The search index (I22): the active model, stored vectors per model and locale, reindex, deleting an old model. */
export function AdminEmbeddingsPage() {
  const { t } = useTranslation();
  const state = api.useQuery("get", "/api/admin/embeddings", undefined, {
    refetchInterval: (query) =>
      query.state.data?.job && isActive(query.state.data.job.state) ? activeJobPollMs : false,
  });

  if (state.isPending) {
    return (
      <div role="status" aria-label={t("common.loading")}>
        <Skeleton className="h-40 w-full" />
      </div>
    );
  }
  if (state.isError) {
    return <ErrorState onRetry={() => void state.refetch()} />;
  }

  return (
    <div className="flex flex-col gap-4">
      <p className="max-w-3xl text-sm text-muted-foreground">{t("admin.embeddings.description")}</p>
      <div className="grid gap-4 lg:grid-cols-2">
        <ActiveModel state={state.data} />
        <ReindexSection activeProfile={state.data.activeProfile} />
      </div>
      <StoredVectors state={state.data} />
    </div>
  );
}

function ActiveModel({ state }: { state: EmbeddingIndexState }) {
  const { t } = useTranslation();
  const job = state.job;
  return (
    <AdminSection title={t("admin.embeddings.active.title")}>
      <dl className="grid grid-cols-[auto_1fr] gap-x-6 gap-y-1 text-sm">
        <dt className="text-muted-foreground">{t("admin.embeddings.active.profile")}</dt>
        <dd>{state.activeProfile}</dd>
        <dt className="text-muted-foreground">{t("admin.embeddings.active.model")}</dt>
        <dd className="break-all">{state.activeModel}</dd>
        <dt className="text-muted-foreground">{t("admin.embeddings.active.dimensions")}</dt>
        <dd className="tabular-nums">{state.activeDimensions}</dd>
      </dl>
      {job && (
        <div className="flex flex-col gap-1 border-t pt-3 text-sm">
          <span className="font-medium">{t("admin.embeddings.lastJob")}</span>
          <div className="flex flex-wrap items-center gap-2">
            <JobStateBadge state={job.state} />
            <span>
              {jobSummary(
                {
                  kind: "embeddingReindex",
                  state: job.state,
                  progress: job.progress,
                  result: job.result,
                  error: job.error,
                },
                t,
              )}
            </span>
            <Link to={`/admin/jobs/${job.id}`} className="underline underline-offset-4">
              {t("admin.jobs.details")}
            </Link>
          </div>
        </div>
      )}
    </AdminSection>
  );
}

function ReindexSection({ activeProfile }: { activeProfile: string }) {
  const { t } = useTranslation();
  const id = useId();
  const refresh = useRefreshAdmin();
  const profiles = api.useQuery("get", "/api/admin/ai/profiles");
  const [profile, setProfile] = useState(activeProfile);
  const [onlyStale, setOnlyStale] = useState(true);
  const [locales, setLocales] = useState<string[]>([]);
  const reindex = api.useMutation("post", "/api/admin/embeddings/reindex", {
    onSettled: () => void refresh(),
  });

  const onSubmit = (event: SubmitEvent<HTMLFormElement>) => {
    event.preventDefault();
    reindex.mutate({
      body: { profile, onlyStale, locales: locales.length > 0 ? locales : null },
    });
  };
  const conflictId = reindex.isError ? conflictingJobId(reindex.error) : undefined;
  const profileNames = profiles.data?.embeddingProfiles.map((p) => p.name) ?? [activeProfile];

  return (
    <AdminSection
      title={t("admin.embeddings.reindex.title")}
      description={t("admin.embeddings.reindex.description")}
    >
      <form onSubmit={onSubmit} className="flex flex-col gap-3">
        <SelectField
          label={t("admin.embeddings.reindex.profile")}
          value={profile}
          onChange={setProfile}
        >
          {profileNames.map((name) => (
            <option key={name} value={name}>
              {name === activeProfile ? `${name} (${t("admin.embeddings.sets.active")})` : name}
            </option>
          ))}
        </SelectField>
        <div className="flex items-center gap-2">
          <Checkbox
            id={`${id}-stale`}
            checked={onlyStale}
            onCheckedChange={(checked) => {
              setOnlyStale(checked === true);
            }}
          />
          <Label htmlFor={`${id}-stale`} className="font-normal">
            {t("admin.embeddings.reindex.onlyStale")}
          </Label>
        </div>
        <fieldset className="flex flex-col gap-1">
          <legend className="mb-1 text-sm font-medium">
            {t("admin.embeddings.reindex.locales")}
          </legend>
          <div className="flex gap-4">
            {supportedLanguages.map((locale) => (
              <div key={locale} className="flex items-center gap-2">
                <Checkbox
                  id={`${id}-${locale}`}
                  checked={locales.includes(locale)}
                  onCheckedChange={(checked) => {
                    setLocales((current) =>
                      checked === true
                        ? [...current, locale]
                        : current.filter((value) => value !== locale),
                    );
                  }}
                />
                <Label htmlFor={`${id}-${locale}`} className="font-normal">
                  {locale}
                </Label>
              </div>
            ))}
          </div>
          <p className="text-xs text-muted-foreground">
            {t("admin.embeddings.reindex.localesHint")}
          </p>
        </fieldset>
        <div>
          <Button type="submit" disabled={reindex.isPending}>
            {t("admin.embeddings.reindex.submit")}
          </Button>
        </div>
      </form>
      {reindex.data && (
        <Alert>
          <CheckCircle2 aria-hidden />
          <AlertDescription>
            <p>
              {t("admin.embeddings.reindex.queued")}{" "}
              <Link to={`/admin/jobs/${reindex.data.id}`} className="underline underline-offset-4">
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
              {t("admin.embeddings.reindex.conflict")}{" "}
              <Link to={`/admin/jobs/${conflictId}`} className="underline underline-offset-4">
                {t("admin.catalog.import.openJob")}
              </Link>
            </p>
          </AlertDescription>
        </Alert>
      )}
      {reindex.isError && !conflictId && <ErrorState />}
    </AdminSection>
  );
}

interface ModelVectors {
  model: string;
  dimensions: number;
  sets: EmbeddingSetStats[];
}

function byModel(sets: EmbeddingSetStats[]): ModelVectors[] {
  const models = new Map<string, ModelVectors>();
  for (const set of sets) {
    const entry = models.get(set.model) ?? {
      model: set.model,
      dimensions: set.dimensions,
      sets: [],
    };
    entry.sets.push(set);
    models.set(set.model, entry);
  }
  return [...models.values()];
}

function StoredVectors({ state }: { state: EmbeddingIndexState }) {
  const { t, i18n } = useTranslation();
  const refresh = useRefreshAdmin();
  const remove = api.useMutation("delete", "/api/admin/embeddings", {
    onSettled: () => void refresh(),
  });
  const number = new Intl.NumberFormat(i18n.language);
  const models = byModel(state.sets);

  return (
    <AdminSection title={t("admin.embeddings.sets.title")}>
      {models.length === 0 ? (
        <EmptyState>{t("admin.embeddings.sets.empty")}</EmptyState>
      ) : (
        <div className="overflow-x-auto">
          <table className="w-full text-sm">
            <thead className="text-left text-xs text-muted-foreground">
              <tr>
                <th className="py-2 pr-3 font-medium">{t("admin.embeddings.sets.model")}</th>
                <th className="py-2 pr-3 font-medium">{t("admin.embeddings.sets.dimensions")}</th>
                <th className="py-2 pr-3 font-medium">{t("admin.embeddings.sets.count")}</th>
                <th className="py-2">
                  <span className="sr-only">{t("admin.jobs.details")}</span>
                </th>
              </tr>
            </thead>
            <tbody>
              {models.map((entry) => {
                const active = entry.model === state.activeModel;
                return (
                  <tr key={entry.model} className="border-t align-top">
                    <td className="py-2 pr-3 break-all">
                      {entry.model}{" "}
                      {active && (
                        <Badge variant="secondary">{t("admin.embeddings.sets.active")}</Badge>
                      )}
                    </td>
                    <td className="py-2 pr-3 tabular-nums">{entry.dimensions}</td>
                    <td className="py-2 pr-3 tabular-nums">
                      {entry.sets
                        .map((set) => `${set.locale} ${number.format(set.count)}`)
                        .join(" · ")}
                    </td>
                    <td className="py-2 text-right">
                      {!active && (
                        <Button
                          variant="outline"
                          size="sm"
                          disabled={remove.isPending}
                          aria-label={t("admin.embeddings.sets.delete", { model: entry.model })}
                          onClick={() => {
                            if (
                              window.confirm(
                                t("admin.embeddings.sets.confirmDelete", { model: entry.model }),
                              )
                            ) {
                              remove.mutate({ params: { query: { model: entry.model } } });
                            }
                          }}
                        >
                          <Trash2 aria-hidden />
                        </Button>
                      )}
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      )}
      {remove.data && (
        <p className="text-sm text-muted-foreground" role="status">
          {t("admin.embeddings.sets.deleted", {
            model: remove.data.model,
            deleted: remove.data.deleted,
          })}
        </p>
      )}
      {remove.isError && <ErrorState />}
    </AdminSection>
  );
}
