import { useTranslation } from "@neuroscan/i18n";
import { Badge, Card } from "@neuroscan/ui";
import { RoleGuard } from "../../features/auth/components/RoleGuard";
import { useCurrentUser } from "../../features/auth/hooks/useCurrentUser";
import { formatHospitalLabel, formatRoleLabel } from "../../features/auth/utils/user-display";

export function AuthHomePage() {
  const { t } = useTranslation();
  const { data: user } = useCurrentUser();

  const roleLabel = formatRoleLabel(t, user?.role);
  const hospitalDisplay = formatHospitalLabel(user, t("portal.home.notProvided"));

  return (
    <div style={{ display: "grid", gap: "1.5rem" }}>
      <div>
        <h1 style={{ margin: 0 }}>{t("portal.home.title")}</h1>
        <p style={{ marginTop: "0.375rem", color: "#475569" }}>{t("portal.home.subtitle")}</p>
      </div>

      <Card heading={t("portal.home.profileHeading")}>
        <dl
          style={{
            margin: 0,
            display: "grid",
            gridTemplateColumns: "repeat(auto-fit, minmax(14rem, 1fr))",
            gap: "1rem",
          }}
        >
          <div>
            <dt style={{ fontSize: "0.75rem", color: "#64748b", textTransform: "uppercase" }}>
              {t("portal.home.fullNameLabel")}
            </dt>
            <dd style={{ margin: "0.25rem 0 0", fontWeight: 600 }}>{user?.fullName ?? "-"}</dd>
          </div>

          <div>
            <dt style={{ fontSize: "0.75rem", color: "#64748b", textTransform: "uppercase" }}>
              {t("portal.home.roleLabel")}
            </dt>
            <dd style={{ margin: "0.25rem 0 0" }}>
              <Badge tone="info">{roleLabel}</Badge>
            </dd>
          </div>

          <div>
            <dt style={{ fontSize: "0.75rem", color: "#64748b", textTransform: "uppercase" }}>
              {t("portal.home.hospitalLabel")}
            </dt>
            <dd style={{ margin: "0.25rem 0 0", fontWeight: 500 }}>{hospitalDisplay}</dd>
          </div>

          <div>
            <dt style={{ fontSize: "0.75rem", color: "#64748b", textTransform: "uppercase" }}>
              {t("portal.home.cccdLabel")}
            </dt>
            <dd style={{ margin: "0.25rem 0 0", fontFamily: "monospace" }}>{user?.cccd ?? "-"}</dd>
          </div>

          <div>
            <dt style={{ fontSize: "0.75rem", color: "#64748b", textTransform: "uppercase" }}>
              {t("portal.home.cchnLabel")}
            </dt>
            <dd style={{ margin: "0.25rem 0 0", fontFamily: "monospace" }}>
              {user?.maCchn || t("portal.home.notProvided")}
            </dd>
          </div>
        </dl>
      </Card>

      <Card heading={t("portal.home.modulesHeading")} description={t("portal.home.modulesDescription")}>
        <div style={{ display: "grid", gap: "0.75rem" }}>
          <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", gap: "1rem", padding: "0.75rem", border: "1px solid #e2e8f0", borderRadius: "0.5rem" }}>
            <span style={{ fontWeight: 500 }}>{t("portal.home.worklistModuleTitle")}</span>
            <Badge tone="neutral">{t("portal.home.worklistModuleStatus")}</Badge>
          </div>

          <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", gap: "1rem", padding: "0.75rem", border: "1px solid #e2e8f0", borderRadius: "0.5rem" }}>
            <span style={{ fontWeight: 500 }}>{t("portal.home.segmentationModuleTitle")}</span>
            <Badge tone="neutral">{t("portal.home.segmentationModuleStatus")}</Badge>
          </div>

          <RoleGuard allowedRoles={["HOSPITAL_ADMIN", "MOH_ADMIN"]} fallback={null}>
            <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", gap: "1rem", padding: "0.75rem", border: "1px solid #bae6fd", borderRadius: "0.5rem", backgroundColor: "#f0f9ff" }}>
              <span style={{ fontWeight: 600 }}>{t("portal.home.adminModuleTitle")}</span>
              <Badge tone="success">{t("portal.home.adminModuleStatus")}</Badge>
            </div>
          </RoleGuard>
        </div>
      </Card>
    </div>
  );
}
