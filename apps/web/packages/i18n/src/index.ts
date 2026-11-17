export { I18nProvider } from "./I18nProvider";
export type { I18nProviderProps } from "./I18nProvider";
export { LanguageSwitcher } from "./LanguageSwitcher";
export type { LanguageSwitcherProps } from "./LanguageSwitcher";
export { useTranslation } from "react-i18next";
export {
  DEFAULT_LANGUAGE,
  getCurrentLanguage,
  i18n,
  LANGUAGE_STORAGE_KEY,
  resolveLanguage,
  setLanguage,
  SUPPORTED_LANGUAGES,
  useLocale,
} from "./i18n";
export type { Language } from "./i18n";
export { I18nUiLocalizationProvider } from "./UiI18nProvider";
export type { I18nUiLocalizationProviderProps } from "./UiI18nProvider";
export { createUiLocalizationConfig } from "./ui-localization";
export { defineResources, registerResources } from "./resource-registration";
export type {
  LocalizedResources,
  ResourceRegistration,
} from "./resource-registration";
