import type { ReactNode } from "react";
import { I18nextProvider } from "react-i18next";

import { i18n } from "./i18n";
import { I18nUiLocalizationProvider } from "./UiI18nProvider";

export interface I18nProviderProps {
  children: ReactNode;
}

export function I18nProvider({ children }: I18nProviderProps) {
  return (
    <I18nextProvider i18n={i18n}>
      <I18nUiLocalizationProvider>{children}</I18nUiLocalizationProvider>
    </I18nextProvider>
  );
}
