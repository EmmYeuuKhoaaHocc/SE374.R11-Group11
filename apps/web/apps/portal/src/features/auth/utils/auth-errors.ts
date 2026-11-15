export function resolveAuthErrorMessage(
  t: (key: string, options?: Record<string, unknown>) => string,
  code?: string | null,
  fallbackDetail?: string | null,
): string {
  if (code) {
    const key = `auth.errors.${code}`;
    const translated = t(key, { defaultValue: "" });
    if (translated) {
      return translated;
    }
  }

  if (fallbackDetail && fallbackDetail.trim().length > 0) {
    return fallbackDetail.trim();
  }

  return t("auth.errors.default");
}
