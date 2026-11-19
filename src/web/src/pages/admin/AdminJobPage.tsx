import { ArrowLeft } from "lucide-react";
import type { ReactNode } from "react";
import { useTranslation } from "react-i18next";
import { Link, useParams } from "react-router";
import { api } from "@/api/client";
import { problemStatus } from "@/api/problem";
import { EmptyState, ErrorState } from "@/components/StatusMessages";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import {
  activeJobPollMs,
  isActive,
  jobDuration,
  jobSummary,
  useRefreshAdmin,
  type JobStatus,
} from "@/features/admin/admin";
import { AdminSection, JobStateBadge } from "@/features/admin/AdminLayout";
import { formatDateTime } from "@/lib/format";

/** One background job (I22): state, timestamps, parameters, progress, result and error; polled while active. */
export function AdminJobPage() {
  const { t } = useTranslation();
  const { id = "" } = useParams();
  const job = api.useQuery(
    "get",
    "/api/admin/jobs/{id}",
    { params: { path: { id } } },
    {
      refetchInterval: (query) =>
        query.state.data && isActive(query.state.data.state) ? activeJobPollMs : false,
    },
  );

  return (
    <div className="flex flex-col gap-4">
      <div>
        <Button asChild variant="ghost" size="sm">
          <Link to="/admin/jobs">
            <ArrowLeft aria-hidden />
            {t("admin.jobs.job.back")}
          </Link>
        </Button>
      </div>
      {job.isPending ? (
        <div role="status" aria-label={t("common.loading")}>
          <Skeleton className="h-40 w-full" />
        </div>
      ) : job.isError ? (
        problemStatus(job.error) === 404 ? (
          <EmptyState>{t("admin.jobs.job.notFound")}</EmptyState>
        ) : (
          <ErrorState onRetry={() => void job.refetch()} />
        )
      ) : (
        <JobDetails job={job.data} />
      )}
    </div>
  );
}

function JobDetails({ job }: { job: JobStatus }) {
  const { t, i18n } = useTranslation();
  const refresh = useRefreshAdmin();
  const cancel = api.useMutation("post", "/api/admin/jobs/{id}/cancel", {
    onSettled: () => void refresh(),
  });
  const time = (value: string | null | undefined) =>
    value ? formatDateTime(value, i18n.language) : "–";

  return (
    <>
      <div className="flex flex-wrap items-center gap-3">
        <h2 className="text-lg font-semibold">
          {t(`admin.jobs.kind.${job.kind}`)} <JobStateBadge state={job.state} />
        </h2>
        <code className="text-xs text-muted-foreground">{job.id}</code>
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
      </div>
      <p>{jobSummary(job, t)}</p>
      {job.cancelRequested === true && isActive(job.state) && (
        <p className="text-sm text-muted-foreground">{t("admin.jobs.cancelRequested")}</p>
      )}

      <dl className="grid grid-cols-[auto_1fr] gap-x-6 gap-y-1 text-sm sm:grid-cols-[auto_1fr_auto_1fr]">
        <Field label={t("admin.jobs.columns.trigger")}>
          {t(`admin.jobs.trigger.${job.trigger}`)}
        </Field>
        <Field label={t("admin.jobs.columns.queuedAt")}>{time(job.queuedAt)}</Field>
        <Field label={t("admin.jobs.job.startedAt")}>{time(job.startedAt)}</Field>
        <Field label={t("admin.jobs.job.finishedAt")}>{time(job.finishedAt)}</Field>
        <Field label={t("admin.jobs.job.heartbeatAt")}>{time(job.heartbeatAt)}</Field>
        <Field label={t("admin.jobs.columns.duration")}>
          {jobDuration(job, i18n.language) || "–"}
        </Field>
        <Field label={t("admin.jobs.job.attempts")}>{job.attempts ?? 0}</Field>
        <Field label={t("admin.jobs.job.worker")}>{job.workerId ?? "–"}</Field>
      </dl>

      {job.error && (
        <AdminSection title={t("admin.jobs.job.error")} className="border-destructive/50">
          <pre className="text-sm whitespace-pre-wrap text-destructive">{job.error}</pre>
        </AdminSection>
      )}
      <div className="grid gap-4 lg:grid-cols-3">
        <JsonBlock title={t("admin.jobs.job.parameters")} value={job.parameters} />
        <JsonBlock title={t("admin.jobs.job.progress")} value={job.progress} />
        <JsonBlock title={t("admin.jobs.job.result")} value={job.result} />
      </div>
    </>
  );
}

function Field({ label, children }: { label: string; children: ReactNode }) {
  return (
    <>
      <dt className="text-muted-foreground">{label}</dt>
      <dd className="tabular-nums">{children}</dd>
    </>
  );
}

function JsonBlock({ title, value }: { title: string; value: unknown }) {
  if (value === null || value === undefined) {
    return null;
  }
  return (
    <AdminSection title={title}>
      <pre className="max-h-96 overflow-auto rounded-md bg-muted p-2 font-mono text-xs">
        {JSON.stringify(value, null, 2)}
      </pre>
    </AdminSection>
  );
}
