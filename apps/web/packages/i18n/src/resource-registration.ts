import type { ResourceLanguage } from "i18next";

import { i18n, SUPPORTED_LANGUAGES, type Language } from "./i18n";

export type LocalizedResources = Readonly<Record<Language, ResourceLanguage>>;

export interface ResourceRegistration {
  source: string;
  resources: LocalizedResources;
}

export function defineResources<const T extends LocalizedResources>(resources: T): T {
  return resources;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

function flattenKeys(
  value: unknown,
  prefix = "",
  result: string[] = [],
): string[] {
  if (!isRecord(value) || Object.keys(value).length === 0) {
    if (prefix) result.push(prefix);
    return result;
  }

  for (const [key, child] of Object.entries(value)) {
    flattenKeys(child, prefix ? `${prefix}.${key}` : key, result);
  }

  return result;
}

function validateRegistration({ source, resources }: ResourceRegistration): string[] {
  for (const language of SUPPORTED_LANGUAGES) {
    if (!isRecord(resources[language])) {
      throw new Error(`[i18n] ${source} must define resources for ${language}.`);
    }
  }

  const [baseLanguage, ...otherLanguages] = SUPPORTED_LANGUAGES;
  const baseKeys = flattenKeys(resources[baseLanguage]).sort();

  for (const language of otherLanguages) {
    const localizedKeys = flattenKeys(resources[language]).sort();
    const missing = baseKeys.filter((key) => !localizedKeys.includes(key));
    const extra = localizedKeys.filter((key) => !baseKeys.includes(key));

    if (missing.length > 0 || extra.length > 0) {
      throw new Error(
        `[i18n] ${source} has mismatched locale keys for ${language}; ` +
          `missing: ${missing.join(", ") || "none"}; extra: ${extra.join(", ") || "none"}.`,
      );
    }
  }

  const commonKeys = baseKeys.filter((key) => key === "common" || key.startsWith("common."));
  if (commonKeys.length > 0) {
    throw new Error(
      `[i18n] ${source} cannot register common resources. ` +
        "Shared common copy belongs to @neuroscan/i18n.",
    );
  }

  return baseKeys;
}

export function registerResources(registrations: readonly ResourceRegistration[]): void {
  const owners = new Map<string, string>();
  const validated = [...registrations]
    .sort((left, right) => left.source.localeCompare(right.source))
    .map((registration) => ({
      registration,
      keys: validateRegistration(registration),
    }));

  for (const { registration, keys } of validated) {
    for (const key of keys) {
      const existingOwner = owners.get(key);
      if (existingOwner) {
        throw new Error(
          `[i18n] Duplicate translation key "${key}" in ${existingOwner} and ${registration.source}.`,
        );
      }
      owners.set(key, registration.source);
    }
  }

  for (const { registration } of validated) {
    for (const language of SUPPORTED_LANGUAGES) {
      i18n.addResourceBundle(
        language,
        "translation",
        registration.resources[language],
        true,
        true,
      );
    }
  }
}
