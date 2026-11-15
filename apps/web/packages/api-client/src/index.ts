import axios, { AxiosHeaders, type AxiosInstance, type CreateAxiosDefaults } from "axios";

import { applyLanguageHeader, normalizeApiError } from "./api-error";

export interface ApiClientConfig extends CreateAxiosDefaults {
  getLanguage?: () => string;
}

export function createApiClient(config: ApiClientConfig = {}): AxiosInstance {
  const { getLanguage, ...axiosConfig } = config;
  const client = axios.create({
    withCredentials: true,
    ...axiosConfig,
  });

  if (getLanguage) {
    client.interceptors.request.use((requestConfig) => {
      const headers = AxiosHeaders.from(requestConfig.headers);
      applyLanguageHeader(headers, getLanguage());
      requestConfig.headers = headers;
      return requestConfig;
    });
  }

  client.interceptors.response.use(
    (response) => response,
    (error) => Promise.reject(normalizeApiError(error)),
  );

  return client;
}

export {
  ApiError,
  isApiError,
  normalizeApiError,
  type ApiErrorKind,
  type ApiErrorOptions,
  type ApiErrorResponsePayload,
} from "./api-error";
export { assertSuccess, unwrap } from "./response";
export type { ApiResponse } from "./types/api-response";
export type {
  CursorPaginatedData,
  CursorPaginationMetadata,
  CursorPaginationParams,
  InitialCursorPaginationParams,
  PaginatedData,
  TablePaginatedData,
  TablePaginationMetadata,
  TablePaginationParams,
} from "./types/pagination";
