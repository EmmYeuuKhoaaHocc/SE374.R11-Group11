import { apiClient } from "../../../app/api-client";

export type UserRole = "DOCTOR_USER" | "HOSPITAL_ADMIN" | "MOH_ADMIN";

export interface CurrentUser {
  userId: string;
  cccd: string;
  fullName: string;
  role: UserRole | string;
  hospitalId: string;
  maCskcb: string;
  hospitalName: string;
  maCchn: string | null;
}

export interface ExchangeSessionPayload {
  code: string;
  state: string;
}

export interface ExchangeSessionResult {
  userId: string;
  fullName: string;
  role: UserRole | string;
  maCskcb: string;
  hospitalName: string;
  maCchn: string | null;
  returnUrl: string;
}

export function getVneidLoginUrl(returnUrl?: string | null): string {
  if (!returnUrl || !isSafeLocalReturnUrl(returnUrl)) {
    return "/api/auth/login";
  }

  return `/api/auth/login?returnUrl=${encodeURIComponent(returnUrl)}`;
}

export function isSafeLocalReturnUrl(value: string | null | undefined): value is string {
  if (!value) {
    return false;
  }

  const trimmed = value.trim();
  return trimmed.startsWith("/") && !trimmed.startsWith("//") && !trimmed.startsWith("/\\");
}

export async function exchangeSession(payload: ExchangeSessionPayload): Promise<ExchangeSessionResult> {
  const response = await apiClient.post<ExchangeSessionResult>("/api/auth/exchange-session", payload);
  return response.data;
}

export async function getCurrentUser(): Promise<CurrentUser> {
  const response = await apiClient.get<CurrentUser>("/api/me");
  return response.data;
}

export async function logout(): Promise<void> {
  await apiClient.post("/api/auth/logout");
}
