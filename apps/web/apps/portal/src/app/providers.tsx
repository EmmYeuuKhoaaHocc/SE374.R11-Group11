import type { ReactNode } from "react";
import { I18nProvider, I18nUiLocalizationProvider } from "@neuroscan/i18n";
import { QueryClientProvider } from "@tanstack/react-query";
import { ToastProvider } from "@neuroscan/ui";
import { queryClient } from "./query-client";

interface AppProvidersProps {
  children: ReactNode;
}

export function AppProviders({ children }: AppProvidersProps) {
  return (
    <ToastProvider>
      <I18nProvider>
        <I18nUiLocalizationProvider>
          <QueryClientProvider client={queryClient}>
            {children}
          </QueryClientProvider>
        </I18nUiLocalizationProvider>
      </I18nProvider>
    </ToastProvider>
  );
}
