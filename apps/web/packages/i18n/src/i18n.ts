import i18next, { type i18n as I18nInstance } from "i18next";
import { initReactI18next, useTranslation } from "react-i18next";
import { useCallback } from "react";

import { commonResources } from "./common-resources";

export const SUPPORTED_LANGUAGES = ["en-US", "vi-VN"] as const;
export type Language = (typeof SUPPORTED_LANGUAGES)[number];
export const DEFAULT_LANGUAGE: Language = "en-US";
export const LANGUAGE_STORAGE_KEY = "neuroscan.language";

export function resolveLanguage(value: string | null | undefined): Language {
  if (!value) {
    return DEFAULT_LANGUAGE;
  }

  const normalized = value.trim().toLowerCase();
  const exact = SUPPORTED_LANGUAGES.find(
    (language) => language.toLowerCase() === normalized,
  );

  if (exact) {
    return exact;
  }

  const baseLanguage = normalized.split("-")[0];
  return SUPPORTED_LANGUAGES.find(
    (language) => language.split("-")[0].toLowerCase() === baseLanguage,
  ) ?? DEFAULT_LANGUAGE;
}

function readInitialLanguage(): Language {
  if (typeof window !== "undefined") {
    const storedLanguage = window.localStorage.getItem(LANGUAGE_STORAGE_KEY);
    if (storedLanguage) {
      return resolveLanguage(storedLanguage);
    }

    return resolveLanguage(window.navigator.language);
  }

  return DEFAULT_LANGUAGE;
}

export const i18n: I18nInstance = i18next
  .createInstance()
  .use(initReactI18next);

void i18n.init({
  resources: commonResources,
  lng: readInitialLanguage(),
  fallbackLng: DEFAULT_LANGUAGE,
  supportedLngs: SUPPORTED_LANGUAGES,
  defaultNS: "translation",
  interpolation: { escapeValue: false },
  initImmediate: false,
});

export function getCurrentLanguage(): Language {
  return resolveLanguage(i18n.resolvedLanguage ?? i18n.language);
}

export async function setLanguage(language: Language): Promise<Language> {
  const nextLanguage = resolveLanguage(language);
  await i18n.changeLanguage(nextLanguage);

  if (typeof window !== "undefined") {
    window.localStorage.setItem(LANGUAGE_STORAGE_KEY, nextLanguage);
  }

  return nextLanguage;
}

export function useLocale() {
  const { i18n: instance, ...translation } = useTranslation();
  const language = resolveLanguage(instance.resolvedLanguage ?? instance.language);
  const changeLanguage = useCallback(
    (nextLanguage: Language) => setLanguage(nextLanguage),
    [],
  );

  return {
    ...translation,
    language,
    changeLanguage,
    i18n: instance,
  };
}
