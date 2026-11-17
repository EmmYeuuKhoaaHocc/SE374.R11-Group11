import { useQuery } from "@tanstack/react-query";
import { getCurrentUser, type CurrentUser } from "../api/auth.api";

export const authKeys = {
  all: ["auth"] as const,
  me: () => [...authKeys.all, "me"] as const,
};

export function useCurrentUser() {
  return useQuery<CurrentUser, Error>({
    queryKey: authKeys.me(),
    queryFn: getCurrentUser,
    staleTime: 60 * 1000,
    gcTime: 5 * 60 * 1000,
    retry: false,
  });
}
