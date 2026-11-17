import axios, { AxiosError, type AxiosHeaders } from "axios";

export type ApiErrorKind = "http" | "network" | "timeout" | "cancelled" | "unknown";

export interface ApiErrorOptions {
  kind: ApiErrorKind;
  status?: number;
  retryable: boolean;
  code?: string;
  errors?: string[];
  cause?: unknown;
}

export interface ApiErrorResponsePayload {
  detail?: unknown;
  title?: unknown;
  message?: unknown;
  code?: unknown;
  errors?: unknown;
  status?: unknown;
}

export class ApiError extends Error {
  readonly kind: ApiErrorKind;
  readonly status?: number;
  readonly retryable: boolean;
  readonly code?: string;
  readonly errors: string[];
  override readonly cause?: unknown;

  constructor(message: string, options: ApiErrorOptions) {
    super(message);
    this.name = "ApiError";
    this.kind = options.kind;
    this.status = options.status;
    this.retryable = options.retryable;
    this.code = options.code;
    this.errors = options.errors ?? [];
    this.cause = options.cause;
  }
}

export function isApiError(error: unknown): error is ApiError {
  return error instanceof ApiError;
}

function extractErrorsList(errors: unknown): string[] {
  if (Array.isArray(errors)) {
    return errors.filter((item): item is string => typeof item === "string" && item.trim().length > 0);
  }

  if (typeof errors === "object" && errors !== null) {
    const collected: string[] = [];
    for (const value of Object.values(errors as Record<string, unknown>)) {
      if (Array.isArray(value)) {
        for (const msg of value) {
          if (typeof msg === "string" && msg.trim().length > 0) {
            collected.push(msg);
          }
        }
      } else if (typeof value === "string" && value.trim().length > 0) {
        collected.push(value);
      }
    }
    return collected;
  }

  return [];
}

function resolvePayloadMessage(payload: ApiErrorResponsePayload | undefined, fallback: string): string {
  if (!payload) {
    return fallback;
  }

  if (typeof payload.detail === "string" && payload.detail.trim().length > 0) {
    return payload.detail.trim();
  }

  if (typeof payload.message === "string" && payload.message.trim().length > 0) {
    return payload.message.trim();
  }

  if (typeof payload.title === "string" && payload.title.trim().length > 0) {
    return payload.title.trim();
  }

  return fallback;
}

export function normalizeApiError(error: unknown): ApiError {
  if (error instanceof ApiError) {
    return error;
  }

  if (axios.isCancel(error)) {
    return new ApiError("Request cancelled", {
      kind: "cancelled",
      retryable: false,
      cause: error,
    });
  }

  if (error instanceof AxiosError) {
    if (error.code === "ECONNABORTED" || error.code === "ETIMEDOUT") {
      return new ApiError("Request timed out", {
        kind: "timeout",
        retryable: true,
        code: error.code,
        cause: error,
      });
    }

    if (error.response) {
      const status = error.response.status;
      const payload =
        typeof error.response.data === "object" && error.response.data !== null
          ? (error.response.data as ApiErrorResponsePayload)
          : undefined;
      const errors = extractErrorsList(payload?.errors);
      const code = typeof payload?.code === "string" ? payload.code : undefined;
      const fallbackMessage = errors[0] || error.message || `HTTP ${status}`;
      const message = resolvePayloadMessage(payload, fallbackMessage);

      return new ApiError(message, {
        kind: "http",
        status,
        retryable: status === 429 || status >= 500,
        code,
        errors,
        cause: error,
      });
    }

    if (error.request) {
      return new ApiError("Network error while contacting server", {
        kind: "network",
        retryable: true,
        code: error.code,
        cause: error,
      });
    }
  }

  if (error instanceof Error) {
    return new ApiError(error.message, {
      kind: "unknown",
      retryable: false,
      cause: error,
    });
  }

  return new ApiError("Unexpected error", {
    kind: "unknown",
    retryable: false,
    cause: error,
  });
}

export function applyLanguageHeader(headers: AxiosHeaders, language: string): void {
  headers.set("Accept-Language", language);
}
