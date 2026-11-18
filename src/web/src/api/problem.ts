import type { components } from "./schema";

/**
 * An API error: RFC 9457 Problem Details. Commerce errors add a camelCase `code` (spec 04),
 * validation errors add `errors`. The client middleware turns empty or non-JSON error bodies into this shape too.
 */
export type ApiProblem = components["schemas"]["ProblemDetails"] & {
  code?: string;
  errors?: Record<string, string[]>;
};

/** A failed API call thrown by hand-written calls (the query hooks throw the Problem Details object itself). */
export class ApiError extends Error {
  readonly problem: ApiProblem;

  constructor(problem: ApiProblem) {
    super(problem.title ?? `API error ${String(problem.status ?? "")}`.trim());
    this.name = "ApiError";
    this.problem = problem;
  }
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null;
}

function asProblem(error: unknown): unknown {
  return error instanceof ApiError ? error.problem : error;
}

/** The `code` extension of a Problem Details error, if any. */
export function problemCode(error: unknown): string | undefined {
  const problem = asProblem(error);
  return isRecord(problem) && typeof problem.code === "string" ? problem.code : undefined;
}

/** The HTTP status of a Problem Details error, if any. */
export function problemStatus(error: unknown): number | undefined {
  const problem = asProblem(error);
  return isRecord(problem) && typeof problem.status === "number" ? problem.status : undefined;
}
