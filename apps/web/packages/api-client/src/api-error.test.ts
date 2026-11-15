import { AxiosError, AxiosHeaders, type InternalAxiosRequestConfig } from "axios";
import { describe, expect, it } from "vitest";
import { normalizeApiError } from "./api-error";

function makeHttpAxiosError(status: number, data: unknown): AxiosError {
  const config: InternalAxiosRequestConfig = { headers: new AxiosHeaders() };
  return new AxiosError(
    `Request failed with status code ${status}`,
    "ERR_BAD_REQUEST",
    config,
    {},
    {
      status,
      statusText: "Error",
      headers: {},
      config,
      data,
    },
  );
}

describe("normalizeApiError", () => {
  it("parses RFC 7807 ProblemDetails detail, code, and dictionary validation errors", () => {
    const err = makeHttpAxiosError(400, {
      title: "Validation Failed",
      status: 400,
      detail: "ReturnUrl must be a local relative path.",
      code: "VALIDATION_ERROR",
      errors: {
        ReturnUrl: ["ReturnUrl must be a local relative path."],
      },
    });

    const normalized = normalizeApiError(err);
    expect(normalized.kind).toBe("http");
    expect(normalized.status).toBe(400);
    expect(normalized.code).toBe("VALIDATION_ERROR");
    expect(normalized.message).toBe("ReturnUrl must be a local relative path.");
    expect(normalized.errors).toEqual(["ReturnUrl must be a local relative path."]);
    expect(normalized.retryable).toBe(false);
  });

  it("falls back to title when detail is omitted in ProblemDetails", () => {
    const err = makeHttpAxiosError(403, {
      title: "Permission Denied",
      status: 403,
      code: "NO_CCHN_PERMISSION",
    });

    const normalized = normalizeApiError(err);
    expect(normalized.status).toBe(403);
    expect(normalized.code).toBe("NO_CCHN_PERMISSION");
    expect(normalized.message).toBe("Permission Denied");
  });
});
