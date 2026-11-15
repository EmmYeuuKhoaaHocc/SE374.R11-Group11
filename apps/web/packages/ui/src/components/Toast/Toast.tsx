import { createPortal } from "react-dom";
import { useCallback, useEffect, useRef, useState } from "react";
import styles from "./Toast.module.css";
import {
  ToastContext,
  type ToastItem,
  type ToastOptions,
  type ToastProviderProps,
} from "./toast-context";

export function ToastProvider({ children, duration = 5000, maxToasts = 3 }: ToastProviderProps) {
  const [toasts, setToasts] = useState<ToastItem[]>([]);
  const idRef = useRef(0);

  const dismiss = useCallback((id?: string) => {
    setToasts((currentToasts) => (id ? currentToasts.filter((item) => item.id !== id) : []));
  }, []);

  const toast = useCallback(
    (options: ToastOptions) => {
      idRef.current += 1;
      const id = `toast-${idRef.current}`;
      const nextToast: ToastItem = {
        ...options,
        id,
        duration: options.duration ?? duration,
      };
      setToasts((currentToasts) => [...currentToasts, nextToast].slice(-maxToasts));
      return id;
    },
    [duration, maxToasts],
  );

  const value = { toast, dismiss };

  return (
    <ToastContext.Provider value={value}>
      {children}
      {typeof document === "undefined"
        ? null
        : createPortal(
            <ol className={styles.viewport} aria-label="Notifications">
              {toasts.map((item) => (
                <ToastItemView key={item.id} item={item} onDismiss={dismiss} />
              ))}
            </ol>,
            document.body,
          )}
    </ToastContext.Provider>
  );
}

function ToastItemView({ item, onDismiss }: { item: ToastItem; onDismiss: (id: string) => void }) {
  useEffect(() => {
    if (item.duration === Infinity) return undefined;
    const timeoutId = window.setTimeout(() => onDismiss(item.id), item.duration);
    return () => window.clearTimeout(timeoutId);
  }, [item.duration, item.id, onDismiss]);

  const role = item.variant === "destructive" || item.variant === "warning" ? "alert" : "status";

  return (
    <li className={[styles.toast, styles[item.variant ?? "default"]].join(" ")} role={role}>
      <div className={styles.content}>
        {item.title ? <p className={styles.title}>{item.title}</p> : null}
        {item.description ? <p className={styles.description}>{item.description}</p> : null}
      </div>
      <button
        aria-label="Dismiss notification"
        className={styles.closeButton}
        onClick={() => onDismiss(item.id)}
        type="button"
      >
        <span aria-hidden="true">×</span>
      </button>
    </li>
  );
}
