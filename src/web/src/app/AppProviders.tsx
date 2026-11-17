import { QueryClientProvider, type QueryClient } from "@tanstack/react-query";
import { useEffect, useState, type ReactNode } from "react";
import { useTranslation } from "react-i18next";
import { createQueryClient } from "./queryClient";

interface AppProvidersProps {
  children: ReactNode;
  /** Tests pass their own client (no retries, isolated cache). */
  queryClient?: QueryClient;
}

export function AppProviders({ children, queryClient }: AppProvidersProps) {
  const [client] = useState(() => queryClient ?? createQueryClient());
  const { i18n } = useTranslation();

  // Server data can depend on Accept-Language (translated titles), so it is refetched
  // after the user switches the language.
  useEffect(() => {
    const refetch = () => {
      void client.invalidateQueries();
    };
    i18n.on("languageChanged", refetch);
    return () => {
      i18n.off("languageChanged", refetch);
    };
  }, [client, i18n]);

  return <QueryClientProvider client={client}>{children}</QueryClientProvider>;
}
