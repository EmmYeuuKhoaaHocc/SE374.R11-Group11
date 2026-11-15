import { UiLocalizationProvider } from "@neuroscan/ui";
import { useMemo, type ReactNode } from "react";

import { useLocale } from "./i18n";
import { createUiLocalizationConfig } from "./ui-localization";

export interface I18nUiLocalizationProviderProps {
  children: ReactNode;
}

export function I18nUiLocalizationProvider({ children }: I18nUiLocalizationProviderProps) {
  const { language, t } = useLocale();
  const config = useMemo(
    () => createUiLocalizationConfig(t as (key: string, options?: Record<string, unknown>) => string, language),
    [language, t],
  );

  return <UiLocalizationProvider config={config}>{children}</UiLocalizationProvider>;
}
