import { LanguageSwitcher, useTranslation } from "@neuroscan/i18n";
import { Alert, Button, Card } from "@neuroscan/ui";
import { ShieldCheck } from "lucide-react";
import { useSearchParams } from "react-router";
import { getVneidLoginUrl } from "../api/auth.api";
import { resolveAuthErrorMessage } from "../utils/auth-errors";

export function LoginPage() {
  const { t } = useTranslation();
  const [searchParams] = useSearchParams();

  const errorCode = searchParams.get("errorCode") || searchParams.get("error");
  const errorDescription = searchParams.get("error_description");
  const returnUrl = searchParams.get("returnUrl");
  const errorMessage = errorCode || errorDescription
    ? resolveAuthErrorMessage(t, errorCode, errorDescription)
    : null;

  function handleVneidLogin() {
    window.location.assign(getVneidLoginUrl(returnUrl));
  }

  return (
    <div
      style={{
        minHeight: "100vh",
        display: "flex",
        flexDirection: "column",
        alignItems: "center",
        justifyContent: "center",
        padding: "1.5rem",
        backgroundColor: "#f8fafc",
        gap: "1rem",
      }}
    >
      <div style={{ width: "100%", maxWidth: "30rem", display: "flex", justifyContent: "flex-end" }}>
        <LanguageSwitcher />
      </div>

      <Card style={{ width: "100%", maxWidth: "30rem", padding: "2rem" }}>
        <div style={{ display: "grid", gap: "1.25rem", textAlign: "center" }}>
          <div style={{ display: "flex", justifyContent: "center" }}>
            <div style={{ padding: "0.75rem", borderRadius: "9999px", backgroundColor: "#e0f2fe" }}>
              <ShieldCheck size={32} color="#0284c7" />
            </div>
          </div>

          <div>
            <h1 style={{ fontSize: "1.5rem", margin: 0 }}>{t("auth.login.title")}</h1>
            <p style={{ marginTop: "0.5rem", color: "#475569", fontSize: "0.9375rem" }}>
              {t("auth.login.subtitle")}
            </p>
          </div>

          {errorMessage ? (
            <Alert tone="error" heading={t("auth.login.errorHeading")} style={{ textAlign: "left" }}>
              {errorMessage}
            </Alert>
          ) : null}

          <Button size="lg" variant="primary" onClick={handleVneidLogin} style={{ width: "100%" }}>
            {t("auth.login.vneidButton")}
          </Button>

          <p style={{ fontSize: "0.75rem", color: "#64748b", margin: 0 }}>
            {t("auth.login.securityNote")}
          </p>
        </div>
      </Card>
    </div>
  );
}
