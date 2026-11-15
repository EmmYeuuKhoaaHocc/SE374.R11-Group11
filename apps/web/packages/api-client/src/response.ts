import { ApiError } from "./api-error";
import type { ApiResponse } from "./types/api-response";

export function unwrap<T>(body: ApiResponse<T>, httpStatus: number): T {
  assertSuccess(body, httpStatus);

  if (body.data === null) {
    throw new ApiError("Invalid API response", {
      kind: "http",
      status: httpStatus,
      retryable: httpStatus >= 500,
      errors: body.errors,
    });
  }

  return body.data;
}

export function assertSuccess(body: ApiResponse<unknown>, httpStatus: number): void {
  if (!body?.isSuccess) {
    throw new ApiError(body?.message || "Invalid API response", {
      kind: "http",
      status: httpStatus,
      retryable: httpStatus >= 500,
      errors: body?.errors ?? [],
    });
  }
}
