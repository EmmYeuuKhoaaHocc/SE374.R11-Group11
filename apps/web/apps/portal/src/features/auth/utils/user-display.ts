import type { CurrentUser } from "../api/auth.api";

export function formatHospitalLabel(user: CurrentUser | null | undefined, fallback: string): string {
  if (!user) {
    return fallback;
  }

  return user.hospitalName ? `${user.hospitalName} (${user.maCskcb})` : user.maCskcb;
}

export function formatRoleLabel(
  t: (key: string, options?: { defaultValue?: string }) => string,
  role: string | null | undefined,
): string {
  if (!role) {
    return "";
  }

  return t(`portal.roles.${role}`, { defaultValue: role });
}
