import { createApiClient } from "@neuroscan/api-client";
import { getCurrentLanguage } from "@neuroscan/i18n";

export const apiClient = createApiClient({
  baseURL: "",
  withCredentials: true,
  getLanguage: () => getCurrentLanguage(),
});
