import type { ReactNode } from "react";
import { useTranslation } from "@neuroscan/i18n";
import { Alert } from "@neuroscan/ui";
import type { UserRole } from "../api/auth.api";
import { useCurrentUser } from "../hooks/useCurrentUser";

export interface RoleGuardProps {
  allowedRoles: readonly UserRole[];
  fallback?: ReactNode;
  children: ReactNode;
}

export function RoleGuard({ allowedRoles, fallback, children }: RoleGuardProps) {
  const { t } = useTranslation();
  const { data: user } = useCurrentUser();

  if (!user || !allowedRoles.includes(user.role as UserRole)) {
    if (fallback !== undefined) {
      return <>{fallback}</>;
    }

    return (
      <Alert tone="error" heading={t("auth.forbidden.title")}>
        {t("auth.forbidden.description")}
      </Alert>
    );
  }

  return <>{children}</>;
}
