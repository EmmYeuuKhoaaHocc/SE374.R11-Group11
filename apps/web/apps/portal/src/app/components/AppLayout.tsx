import { LanguageSwitcher, useTranslation } from "@neuroscan/i18n";
import { Badge, Button } from "@neuroscan/ui";
import { Activity, Building2, LogOut, UserCheck } from "lucide-react";
import { Link, Outlet } from "react-router";
import { useCurrentUser } from "../../features/auth/hooks/useCurrentUser";
import { useLogout } from "../../features/auth/hooks/useLogout";
import { formatHospitalLabel, formatRoleLabel } from "../../features/auth/utils/user-display";

export function AppLayout() {
  const { t } = useTranslation();
  const { data: user } = useCurrentUser();
  const logoutMutation = useLogout();

  const roleLabel = formatRoleLabel(t, user?.role);
  const hospitalBadgeLabel = formatHospitalLabel(user, t("portal.hospitalFallback"));

  return (
    <div style={{ minHeight: "100vh", display: "flex", flexDirection: "column", backgroundColor: "#f8fafc" }}>
      <header
        style={{
          backgroundColor: "#ffffff",
          borderBottom: "1px solid #e2e8f0",
          padding: "0.75rem 1.5rem",
          display: "flex",
          alignItems: "center",
          justifyContent: "space-between",
          flexWrap: "wrap",
          gap: "1rem",
        }}
      >
        <div style={{ display: "flex", alignItems: "center", gap: "1.25rem", flexWrap: "wrap" }}>
          <Link to="/" style={{ display: "flex", alignItems: "center", gap: "0.625rem", textDecoration: "none", color: "#0f172a" }}>
            <Activity size={24} color="#0284c7" />
            <div>
              <div style={{ fontWeight: 700, fontSize: "1.0625rem", lineHeight: 1.2 }}>{t("portal.brand")}</div>
              <div style={{ fontSize: "0.75rem", color: "#64748b" }}>{t("portal.portalTitle")}</div>
            </div>
          </Link>

          <Badge tone="info" style={{ display: "inline-flex", alignItems: "center", gap: "0.35rem" }}>
            <Building2 size={14} />
            <span>{hospitalBadgeLabel}</span>
          </Badge>
        </div>

        <div style={{ display: "flex", alignItems: "center", gap: "0.75rem", flexWrap: "wrap" }}>
          <LanguageSwitcher />
          {user ? (
            <div style={{ display: "flex", alignItems: "center", gap: "0.75rem", borderLeft: "1px solid #e2e8f0", paddingLeft: "0.75rem" }}>
              <div style={{ textAlign: "right" }}>
                <div style={{ fontSize: "0.875rem", fontWeight: 600, color: "#0f172a", display: "flex", alignItems: "center", justifyContent: "flex-end", gap: "0.4rem" }}>
                  <UserCheck size={14} color="#059669" />
                  <span>{user.fullName}</span>
                  {roleLabel ? <Badge tone="neutral">{roleLabel}</Badge> : null}
                </div>
                {user.maCchn ? (
                  <div style={{ fontSize: "0.75rem", color: "#64748b", fontFamily: "monospace" }}>
                    CCHN: {user.maCchn}
                  </div>
                ) : null}
              </div>

              <Button
                variant="outline"
                size="sm"
                disabled={logoutMutation.isPending}
                onClick={() => logoutMutation.mutate()}
                title={t("portal.signOut")}
              >
                <LogOut size={15} />
                <span style={{ marginLeft: "0.35rem" }}>
                  {logoutMutation.isPending ? t("portal.signingOut") : t("portal.signOut")}
                </span>
              </Button>
            </div>
          ) : null}
        </div>
      </header>

      <main style={{ flex: 1, maxWidth: "72rem", width: "100%", margin: "0 auto", padding: "1.5rem" }}>
        <Outlet />
      </main>
    </div>
  );
}
