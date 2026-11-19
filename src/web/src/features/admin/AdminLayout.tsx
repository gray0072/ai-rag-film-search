import { ShieldAlert } from "lucide-react";
import type { ReactNode } from "react";
import { useTranslation } from "react-i18next";
import { Navigate, NavLink, Outlet, useLocation } from "react-router";
import { ErrorState, PageTitle } from "@/components/StatusMessages";
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert";
import { Badge } from "@/components/ui/badge";
import { Skeleton } from "@/components/ui/skeleton";
import { loginUrl, useCurrentUser } from "@/features/auth/auth";
import { cn } from "@/lib/utils";
import { isAdmin, type JobState } from "./admin";

const sections = [
  { to: "/admin", key: "admin.nav.catalog", end: true },
  { to: "/admin/jobs", key: "admin.nav.jobs", end: false },
  { to: "/admin/embeddings", key: "admin.nav.embeddings", end: false },
  { to: "/admin/ai", key: "admin.nav.ai", end: false },
] as const;

/**
 * The admin pages (I22): visitors go to the login page, signed-in users without the `admin` role see "no access".
 * The API checks the role again on every `/api/admin` call; hiding the pages is only for the user.
 */
export function AdminLayout() {
  const { t } = useTranslation();
  const location = useLocation();
  const user = useCurrentUser();

  if (user.isPending) {
    return (
      <div role="status" aria-label={t("common.loading")}>
        <Skeleton className="h-8 w-48" />
      </div>
    );
  }

  if (user.isError) {
    return <ErrorState onRetry={() => void user.refetch()} />;
  }

  if (!user.data.authenticated) {
    return <Navigate to={loginUrl(location.pathname + location.search)} replace />;
  }

  if (!isAdmin(user.data)) {
    return (
      <Alert>
        <ShieldAlert aria-hidden />
        <AlertTitle>{t("admin.forbidden.title")}</AlertTitle>
        <AlertDescription>{t("admin.forbidden.description")}</AlertDescription>
      </Alert>
    );
  }

  return (
    <div className="flex flex-col gap-6">
      <div className="flex flex-col gap-3">
        <PageTitle>{t("admin.title")}</PageTitle>
        <nav aria-label={t("admin.nav.label")} className="flex flex-wrap gap-1 border-b">
          {sections.map((section) => (
            <NavLink
              key={section.to}
              to={section.to}
              end={section.end}
              className={({ isActive }) =>
                cn(
                  "-mb-px border-b-2 px-3 py-2 text-sm outline-none focus-visible:ring-[3px] focus-visible:ring-ring/50",
                  isActive
                    ? "border-primary font-medium text-foreground"
                    : "border-transparent text-muted-foreground hover:text-foreground",
                )
              }
            >
              {t(section.key)}
            </NavLink>
          ))}
        </nav>
      </div>
      <Outlet />
    </div>
  );
}

const stateVariants = {
  queued: "outline",
  running: "secondary",
  succeeded: "default",
  failed: "destructive",
  cancelled: "outline",
} as const;

export function JobStateBadge({ state }: { state: JobState }) {
  const { t } = useTranslation();
  return <Badge variant={stateVariants[state]}>{t(`admin.jobs.state.${state}`)}</Badge>;
}

/** A titled block of an admin page. */
export function AdminSection({
  title,
  description,
  children,
  className,
}: {
  title: string;
  description?: string;
  children: ReactNode;
  className?: string;
}) {
  return (
    <section className={cn("flex flex-col gap-3 rounded-lg border p-4", className)}>
      <div>
        <h2 className="font-semibold">{title}</h2>
        {description && <p className="text-sm text-muted-foreground">{description}</p>}
      </div>
      {children}
    </section>
  );
}
