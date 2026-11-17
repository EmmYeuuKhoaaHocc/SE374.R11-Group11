import { createContext, useContext, type ReactNode } from "react";

export type ToastVariant = "default" | "success" | "warning" | "destructive";

export interface ToastOptions {
  title?: ReactNode;
  description?: ReactNode;
  variant?: ToastVariant;
  duration?: number;
}

export interface ToastItem extends ToastOptions {
  id: string;
}

export interface ToastContextValue {
  toast: (options: ToastOptions) => string;
  dismiss: (id?: string) => void;
}

export interface ToastProviderProps {
  children: ReactNode;
  duration?: number;
  maxToasts?: number;
}

export const ToastContext = createContext<ToastContextValue | null>(null);

export function useToast(): ToastContextValue {
  const context = useContext(ToastContext);
  if (!context) {
    throw new Error("useToast must be used within a ToastProvider.");
  }
  return context;
}
