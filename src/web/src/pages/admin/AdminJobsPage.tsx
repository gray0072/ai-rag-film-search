import { useTranslation } from "react-i18next";
import { Link, useSearchParams } from "react-router";
import { api } from "@/api/client";
import { EmptyState, ErrorState } from "@/components/StatusMessages";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import {
  activeJobPollMs,
  isActive,
  jobDuration,
  jobSummary,
  useRefreshAdmin,
  type JobKind,
  type JobStatus,
} from "@/features/admin/admin";
import { JobStateBadge } from "@/features/admin/AdminLayout";
import { SelectField } from "@/features/filters/FiltersPanel";
import { formatDateTime } from "@/lib/format";

const kinds = ["tmdbImport", "embeddingReindex"] as const satisfies JobKind[];

/** Background jobs (I22): newest first, filtered by kind (`?kind=`), polled while one is active. */
export function AdminJobsPage() {
  const { t } = useTranslation();
  const [searchParams, setSearchParams] = useSearchParams();
  const kind = kinds.find((value) => value === searchParams.get("kind"));
  const jobs = api.useQuery(
    "get",
    "/api/admin/jobs",
    { params: { query: { kind, limit: 50 } } },
    {
      refetchInterval: (query) =>
        query.state.data?.some((job) => isActive(job.state)) ? activeJobPollMs : false,
    },
  );

  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-wrap items-end justify-between gap-3">
        <p className="max-w-2xl text-sm text-muted-foreground">{t("admin.jobs.description")}</p>
        <div className="w-48">
          <SelectField
            label={t("admin.jobs.kind.label")}
            value={kind ?? ""}
            onChange={(value) => {
              const params = new URLSearchParams(searchParams);
              if (value) {
                params.set("kind", value);
              } else {
                params.delete("kind");
              }
              setSearchParams(params);
            }}
          >
            <option value="">{t("admin.jobs.kind.all")}</option>
            {kinds.map((value) => (
              <option key={value} value={value}>
                {t(`admin.jobs.kind.${value}`)}
              </option>
            ))}
          </SelectField>
        </div>
      </div>

      {jobs.isPending ? (
        <div role="status" aria-label={t("common.loading")} className="flex flex-col gap-2">
          <Skeleton className="h-9 w-full" />
          <Skeleton className="h-9 w-full" />
        </div>
      ) : jobs.isError ? (
        <ErrorState onRetry={() => void jobs.refetch()} />
      ) : jobs.data.length === 0 ? (
        <EmptyState>{t("admin.jobs.empty")}</EmptyState>
      ) : (
        <JobTable jobs={jobs.data} />
      )}
    </div>
  );
}

function JobTable({ jobs }: { jobs: JobStatus[] }) {
  const { t, i18n } = useTranslation();
  const refresh = useRefreshAdmin();
  const cancel = api.useMutation("post", "/api/admin/jobs/{id}/cancel", {
    onSettled: () => void refresh(),
  });

  return (
    <div className="overflow-x-auto rounded-lg border">
      <table className="w-full text-sm">
        <caption className="sr-only">{t("admin.jobs.list")}</caption>
        <thead className="bg-muted/50 text-left text-xs text-muted-foreground">
          <tr>
            <th className="px-3 py-2 font-medium">{t("admin.jobs.columns.kind")}</th>
            <th className="px-3 py-2 font-medium">{t("admin.jobs.columns.state")}</th>
            <th className="px-3 py-2 font-medium">{t("admin.jobs.columns.trigger")}</th>
            <th className="px-3 py-2 font-medium">{t("admin.jobs.columns.queuedAt")}</th>
            <th className="px-3 py-2 font-medium">{t("admin.jobs.columns.duration")}</th>
            <th className="px-3 py-2 font-medium">{t("admin.jobs.columns.summary")}</th>
            <th className="px-3 py-2">
              <span className="sr-only">{t("admin.jobs.details")}</span>
            </th>
          </tr>
        </thead>
        <tbody>
          {jobs.map((job) => (
            <tr key={job.id} className="border-t align-top">
              <td className="px-3 py-2 font-medium whitespace-nowrap">
                {t(`admin.jobs.kind.${job.kind}`)}
              </td>
              <td className="px-3 py-2">
                <JobStateBadge state={job.state} />
              </td>
              <td className="px-3 py-2 text-muted-foreground">
                {t(`admin.jobs.trigger.${job.trigger}`)}
              </td>
              <td className="px-3 py-2 whitespace-nowrap text-muted-foreground">
                {formatDateTime(job.queuedAt, i18n.language)}
              </td>
              <td className="px-3 py-2 whitespace-nowrap tabular-nums">
                {jobDuration(job, i18n.language)}
              </td>
              <td className="max-w-md px-3 py-2 break-words">
                {jobSummary(job, t)}
                {job.cancelRequested === true && isActive(job.state) && (
                  <span className="block text-xs text-muted-foreground">
                    {t("admin.jobs.cancelRequested")}
                  </span>
                )}
              </td>
              <td className="px-3 py-2">
                <div className="flex justify-end gap-1">
                  {isActive(job.state) && job.cancelRequested !== true && (
                    <Button
                      variant="outline"
                      size="sm"
                      disabled={cancel.isPending}
                      onClick={() => {
                        cancel.mutate({ params: { path: { id: job.id } } });
                      }}
                    >
                      {t("admin.jobs.cancel")}
                    </Button>
                  )}
                  <Button asChild variant="ghost" size="sm">
                    <Link to={`/admin/jobs/${job.id}`}>{t("admin.jobs.details")}</Link>
                  </Button>
                </div>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
