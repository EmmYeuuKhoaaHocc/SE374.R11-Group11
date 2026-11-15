import type { ReactNode } from "react";
import { Navigate, useLocation } from "react-router";
import { Spinner } from "@neuroscan/ui";
import { useCurrentUser } from "../hooks/useCurrentUser";

interface AuthGuardProps {
  children: ReactNode;
}

export function AuthGuard({ children }: AuthGuardProps) {
  const { data: user, isLoading, isError } = useCurrentUser();
  const location = useLocation();

  if (isLoading) {
    return (
      <div style={{ display: "flex", justifyContent: "center", alignItems: "center", minHeight: "100vh" }}>
        <Spinner size="lg" />
      </div>
    );
  }

  if (isError || !user) {
    const returnUrl = `${location.pathname}${location.search}`;
    return <Navigate to={`/login?returnUrl=${encodeURIComponent(returnUrl)}`} replace />;
  }

  return <>{children}</>;
}
