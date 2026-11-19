import { useQueryClient } from "@tanstack/react-query";
import type { TFunction } from "i18next";
import type { components } from "@/api/schema";
import type { CurrentUser } from "@/api/types";

type Schemas = components["schemas"];
export type JobStatus = Schemas["JobStatus"];
export type JobState = Schemas["JobState"];
export type JobKind = Schemas["JobKind"];

/** The role of the admin pages and the `/api/admin` endpoints (I17). */
export const adminRole = "admin";

export function isAdmin(user: CurrentUser | undefined): boolean {
  return user?.authenticated === true && user.roles.includes(adminRole);
}

/** Refetch every admin query (jobs, embeddings) after a change. */
export function useRefreshAdmin() {
  const queryClient = useQueryClient();
  return () =>
    queryClient.invalidateQueries({
      predicate: (query) =>
        typeof query.queryKey[1] === "string" && query.queryKey[1].startsWith("/api/admin/"),
    });
}

/** How often the job views poll while a job is queued or running. */
export const activeJobPollMs = 2000;

export function isActive(state: JobState): boolean {
  return state === "queued" || state === "running";
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null;
}

function count(record: Record<string, unknown>, key: string): number {
  const value = record[key];
  return typeof value === "number" ? value : 0;
}

const phases = ["genres", "listing", "movies", "embeddings"] as const;

/**
 * One line about a job: its progress while it runs, its counters when it succeeded. Progress and result are JSON whose
 * shape depends on the kind (spec 08 §3).
 */
export function jobSummary(
  job: Pick<JobStatus, "kind" | "state" | "progress" | "result" | "error">,
  t: TFunction,
): string {
  if (job.state === "failed") {
    return job.error ?? "";
  }
  const data = job.state === "succeeded" ? job.result : job.progress;
  if (!isRecord(data)) {
    return "";
  }

  if (job.kind === "embeddingReindex") {
    return job.state === "succeeded"
      ? t("admin.jobs.reindexResult", {
          embedded: count(data, "embedded"),
          unchanged: count(data, "unchanged"),
        })
      : t("admin.jobs.reindexProgress", {
          processed: count(data, "processed"),
          total: count(data, "total"),
        });
  }

  if (job.state === "succeeded") {
    return t("admin.jobs.importResult", {
      created: count(data, "created"),
      updated: count(data, "updated"),
      unchanged: count(data, "unchanged"),
      failed: count(data, "failed"),
    });
  }

  const phase = phases.find((name) => name === data.phase) ?? "movies";
  const embeddings = data.embeddings;
  if (phase === "embeddings" && isRecord(embeddings)) {
    return `${t("admin.jobs.phase.embeddings")}: ${t("admin.jobs.reindexProgress", {
      processed: count(embeddings, "processed"),
      total: count(embeddings, "total"),
    })}`;
  }
  return t("admin.jobs.importProgress", {
    phase: t(`admin.jobs.phase.${phase}`),
    fetched: count(data, "fetched"),
    movies: count(data, "movies"),
  });
}

/** Seconds between start and finish (or now, while it runs), in the UI language; empty before the start. */
export function jobDuration(
  job: Pick<JobStatus, "startedAt" | "finishedAt">,
  language: string,
  now = Date.now(),
): string {
  if (!job.startedAt) {
    return "";
  }
  const end = job.finishedAt ? Date.parse(job.finishedAt) : now;
  const seconds = Math.max(0, Math.round((end - Date.parse(job.startedAt)) / 1000));
  return new Intl.NumberFormat(language, {
    style: "unit",
    unit: "second",
    unitDisplay: "short",
  }).format(seconds);
}

/** TMDB ids typed as "27205, 157336" or one per line; undefined when a part is not a positive whole number. */
export function parseTmdbIds(text: string): number[] | undefined {
  const parts = text.split(/[\s,;]+/).filter((part) => part.length > 0);
  if (parts.length === 0 || parts.some((part) => !/^\d+$/.test(part))) {
    return undefined;
  }
  const ids = parts.map(Number);
  return ids.every((id) => id > 0 && Number.isSafeInteger(id)) ? [...new Set(ids)] : undefined;
}

/** A 409 of the queue endpoints answers with the job that is already queued or running. */
export function conflictingJobId(error: unknown): string | undefined {
  return isRecord(error) && typeof error.id === "string" ? error.id : undefined;
}
