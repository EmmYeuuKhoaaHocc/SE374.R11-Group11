import { I18nProvider } from "@neuroscan/i18n";
import { ToastProvider } from "@neuroscan/ui";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen, within } from "@testing-library/react";
import { createMemoryRouter, RouterProvider } from "react-router";
import { describe, expect, it, vi } from "vitest";
import "../app/register-resources";
import { AppLayout } from "./components/AppLayout";
import { AuthHomePage } from "./pages/AuthHomePage";
import * as authApi from "../features/auth/api/auth.api";

function renderAuthenticatedHome(mockUser: authApi.CurrentUser) {
  vi.spyOn(authApi, "getCurrentUser").mockResolvedValue(mockUser);

  const client = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });

  const router = createMemoryRouter(
    [
      {
        path: "/",
        element: <AppLayout />,
        children: [{ index: true, element: <AuthHomePage /> }],
      },
    ],
    { initialEntries: ["/"] },
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

describe("Authenticated App Shell", () => {
  it("renders dynamic hospital badge and role label without hardcoded Dr prefix", async () => {
    renderAuthenticatedHome({
      userId: "user-1",
      cccd: "079095001122",
      fullName: "Nguyen Van A",
      role: "DOCTOR_USER",
      hospitalId: "hosp-1",
      maCskcb: "79024",
      hospitalName: "Benh vien Cho Ray",
      maCchn: "BYT-001",
    });

    const banner = screen.getByRole("banner");
    expect(await within(banner).findByText("Benh vien Cho Ray (79024)")).toBeInTheDocument();
    expect(screen.getAllByText("Benh vien Cho Ray (79024)")).toHaveLength(2);
    expect(screen.getAllByText("Practitioner").length).toBeGreaterThan(0);
    expect(screen.queryByText(/Dr\./i)).not.toBeInTheDocument();
  });
});
