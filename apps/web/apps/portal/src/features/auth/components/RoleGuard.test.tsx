import { I18nProvider } from "@neuroscan/i18n";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import "../../../app/register-resources";
import * as authApi from "../api/auth.api";
import { RoleGuard } from "./RoleGuard";

function renderWithUser(role: authApi.UserRole, allowedRoles: authApi.UserRole[]) {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });

  vi.spyOn(authApi, "getCurrentUser").mockResolvedValue({
    userId: "u-1",
    cccd: "079095001122",
    fullName: "User One",
    role,
    hospitalId: "h-1",
    maCskcb: "79024",
    hospitalName: "Benh vien Cho Ray",
    maCchn: "BYT-001",
  });

  client.setQueryData(["auth", "me"], {
    userId: "u-1",
    cccd: "079095001122",
    fullName: "User One",
    role,
    hospitalId: "h-1",
    maCskcb: "79024",
    hospitalName: "Benh vien Cho Ray",
    maCchn: "BYT-001",
  });

  return render(
    <I18nProvider>
      <QueryClientProvider client={client}>
        <RoleGuard allowedRoles={allowedRoles}>
          <div>Protected Admin Section</div>
        </RoleGuard>
      </QueryClientProvider>
    </I18nProvider>,
  );
}

describe("RoleGuard", () => {
  it("renders children when user role is included in allowedRoles", () => {
    renderWithUser("MOH_ADMIN", ["HOSPITAL_ADMIN", "MOH_ADMIN"]);
    expect(screen.getByText("Protected Admin Section")).toBeInTheDocument();
  });

  it("renders forbidden alert when user role is excluded", () => {
    renderWithUser("DOCTOR_USER", ["HOSPITAL_ADMIN", "MOH_ADMIN"]);
    expect(screen.queryByText("Protected Admin Section")).not.toBeInTheDocument();
    expect(screen.getByText("Access Restricted")).toBeInTheDocument();
  });
});
