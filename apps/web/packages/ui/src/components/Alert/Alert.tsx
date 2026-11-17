import { forwardRef, useId, type ComponentPropsWithRef, type ReactNode } from "react";

import styles from "./Alert.module.css";

export type AlertTone = "neutral" | "info" | "success" | "warning" | "error";
export type AlertRole = "alert" | "status";

export interface AlertProps extends ComponentPropsWithRef<"div"> {
  tone?: AlertTone;
  heading?: ReactNode;
  role?: AlertRole;
  children: ReactNode;
}

export const Alert = forwardRef<HTMLDivElement, AlertProps>(function Alert({
  "aria-live": ariaLive,
  "aria-labelledby": ariaLabelledby,
  children,
  className,
  heading,
  role,
  tone = "neutral",
  ...props
}, ref) {
  const headingId = useId();
  const resolvedRole = role ?? (tone === "error" || tone === "warning" ? "alert" : "status");
  const classes = [styles.root, styles[tone], className].filter(Boolean).join(" ");

  return (
    <div
      {...props}
      ref={ref}
      className={classes}
      role={resolvedRole}
      aria-live={ariaLive ?? (resolvedRole === "alert" ? "assertive" : "polite")}
      aria-labelledby={heading ? headingId : ariaLabelledby}
    >
      {heading ? <h3 className={styles.heading} id={headingId}>{heading}</h3> : null}
      <div className={styles.content}>{children}</div>
    </div>
  );
});
