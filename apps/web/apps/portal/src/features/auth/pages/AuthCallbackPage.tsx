import { isApiError } from "@neuroscan/api-client";
import { useTranslation } from "@neuroscan/i18n";
import { Alert, Button, Card, Spinner } from "@neuroscan/ui";
import { useQueryClient } from "@tanstack/react-query";
import { useEffect, useMemo, useRef, useState } from "react";
import { useNavigate, useSearchParams } from "react-router";
import { exchangeSession, isSafeLocalReturnUrl } from "../api/auth.api";
import { authKeys } from "../hooks/useCurrentUser";
import { resolveAuthErrorMessage } from "../utils/auth-errors";

interface CallbackFailure {
  code?: string | null;
  detail?: string | null;
}

export function AuthCallbackPage() {
  const { t } = useTranslation();
  const [searchParams] = useSearchParams();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const hasAttempted = useRef(false);

  const oidcError = searchParams.get("error");
  const oidcErrorDescription = searchParams.get("error_description");
  const code = searchParams.get("code");
  const state = searchParams.get("state");

  const initialFailure = useMemo<CallbackFailure | null>(() => {
    if (oidcError) {
      return { code: oidcError, detail: oidcErrorDescription };
    }
    if (!code || !state) {
      return { detail: t("auth.callback.missingParams") };
    }
    return null;
  }, [code, oidcError, oidcErrorDescription, state, t]);

  const [exchangeFailure, setExchangeFailure] = useState<CallbackFailure | null>(null);

  useEffect(() => {
    if (initialFailure || !code || !state || hasAttempted.current) {
      return;
    }

    hasAttempted.current = true;

    void exchangeSession({ code, state })
      .then(async (result) => {
        await queryClient.invalidateQueries({ queryKey: authKeys.me() });
        const destination = isSafeLocalReturnUrl(result.returnUrl) ? result.returnUrl : "/";
        void navigate(destination, { replace: true });
      })
      .catch((error: unknown) => {
        if (isApiError(error)) {
          setExchangeFailure({ code: error.code, detail: error.message });
          return;
        }

        if (error instanceof Error) {
          setExchangeFailure({ detail: error.message });
          return;
        }

        setExchangeFailure({});
      });
  }, [code, initialFailure, navigate, queryClient, state]);

  const failure = initialFailure ?? exchangeFailure;
  const errorMessage = failure
    ? resolveAuthErrorMessage(t, failure.code, failure.detail)
    : null;

  return (
    <div
      style={{
        minHeight: "100vh",
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        padding: "1.5rem",
        backgroundColor: "#f8fafc",
      }}
    >
      <Card style={{ width: "100%", maxWidth: "30rem", padding: "2rem" }}>
        {errorMessage ? (
          <div style={{ display: "grid", gap: "1.25rem" }}>
            <Alert tone="error" heading={t("auth.callback.errorTitle")}>
              {errorMessage}
            </Alert>
            <Button variant="primary" onClick={() => void navigate("/login", { replace: true })}>
              {t("auth.callback.backToLogin")}
            </Button>
          </div>
        ) : (
          <div style={{ display: "grid", gap: "1rem", justifyItems: "center", textAlign: "center" }}>
            <Spinner size="lg" />
            <h1 style={{ fontSize: "1.25rem", margin: 0 }}>{t("auth.callback.processingTitle")}</h1>
            <p style={{ margin: 0, color: "#475569", fontSize: "0.9375rem" }}>
              {t("auth.callback.processingSubtitle")}
            </p>
          </div>
        )}
      </Card>
    </div>
  );
}
