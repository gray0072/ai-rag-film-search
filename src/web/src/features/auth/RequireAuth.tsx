import type { ReactNode } from "react";
import { useTranslation } from "react-i18next";
import { Navigate, useLocation } from "react-router";
import { ErrorState } from "@/components/StatusMessages";
import { Skeleton } from "@/components/ui/skeleton";
import { loginUrl, useCurrentUser } from "./auth";

/** A page for signed-in users: anonymous visitors go to the login page, which brings them back. */
export function RequireAuth({ children }: { children: ReactNode }) {
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

  return children;
}
