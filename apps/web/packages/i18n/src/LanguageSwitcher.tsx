import type { ChangeEvent } from "react";
import { useTranslation } from "react-i18next";

import { SUPPORTED_LANGUAGES, type Language, useLocale } from "./i18n";
import styles from "./LanguageSwitcher.module.css";

export interface LanguageSwitcherProps {
  className?: string;
}

export function LanguageSwitcher({ className }: LanguageSwitcherProps) {
  const { t } = useTranslation();
  const { language, changeLanguage } = useLocale();

  function handleChange(event: ChangeEvent<HTMLSelectElement>) {
    void changeLanguage(event.target.value as Language);
  }

  return (
    <label className={[styles.root, className].filter(Boolean).join(" ")}>
      <span>{t("common.language.label")}</span>
      <span className={styles.selectWrap}>
        <select
          aria-label={t("common.language.label")}
          value={language}
          onChange={handleChange}
        >
          {SUPPORTED_LANGUAGES.map((option) => (
            <option key={option} value={option}>
              {t(`common.language.${option === "en-US" ? "english" : "vietnamese"}`)}
            </option>
          ))}
        </select>
      </span>
    </label>
  );
}
