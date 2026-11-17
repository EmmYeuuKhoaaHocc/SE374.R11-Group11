import { ApiError } from "@neuroscan/api-client";
import { I18nProvider } from "@neuroscan/i18n";
import { ToastProvider } from "@neuroscan/ui";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen } from "@testing-library/react";
import { createMemoryRouter, RouterProvider } from "react-router";
import { describe, expect, it, vi } from "vitest";
import "../../../app/register-resources";
import * as authApi from "../api/auth.api";
import { AuthCallbackPage } from "./AuthCallbackPage";

function renderCallback(initialEntry: string) {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });

  const router = createMemoryRouter(
    [
      { path: "/auth/callback", element: <AuthCallbackPage /> },
      { path: "/login", element: <div>Login Route</div> },
    ],
    { initialEntries: [initialEntry] },
  );

  return render(
    <ToastProvider>
      <I18nProvider>
        <QueryClientProvider client={client}>
          <RouterProvider router={router} />
        </QueryClientProvider>
      </I18nProvider>
    </ToastProvider>,
  );
}

describe("AuthCallbackPage", () => {
  it("renders mapped message when OIDC returns error query parameter without exchanging session", () => {
    const spy = vi.spyOn(authApi, "exchangeSession");
    renderCallback("/auth/callback?error=access_denied&error_description=User+cancelled");

    expect(spy).not.toHaveBeenCalled();
    expect(screen.getByText("VNeID sign-in was cancelled or denied.")).toBeInTheDocument();
  });

  it("renders missing parameter error when code or state is absent", () => {
    const spy = vi.spyOn(authApi, "exchangeSession");
    renderCallback("/auth/callback?state=only-state");

    expect(spy).not.toHaveBeenCalled();
    expect(screen.getByText("Missing OIDC authorization code or state parameter.")).toBeInTheDocument();
  });

  it("maps ApiError NO_CCHN_PERMISSION code from backend ProblemDetails", async () => {
    vi.spyOn(authApi, "exchangeSession").mockRejectedValue(
      new ApiError("Missing CCHN", {
        kind: "http",
        status: 403,
        retryable: false,
        code: "NO_CCHN_PERMISSION",
      }),
    );

    renderCallback("/auth/callback?code=c-1&state=s-1");

    expect(
      await screen.findByText(
        "Citizen does not hold an active CCHN.",
      ),
    ).toBeInTheDocument();
  });
});
