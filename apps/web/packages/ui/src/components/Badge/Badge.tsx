import { forwardRef, type ComponentPropsWithRef, type ReactNode } from "react";

import styles from "./Badge.module.css";

export type BadgeTone = "neutral" | "info" | "success" | "warning" | "error";

export interface BadgeProps extends ComponentPropsWithRef<"span"> {
  tone?: BadgeTone;
  children: ReactNode;
}

export const Badge = forwardRef<HTMLSpanElement, BadgeProps>(function Badge(
  { children, className, tone = "neutral", ...props },
  ref,
) {
  const classes = [styles.root, styles[tone], className].filter(Boolean).join(" ");

  return (
    <span {...props} ref={ref} className={classes}>
      {children}
    </span>
  );
});
