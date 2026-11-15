import { forwardRef, type ComponentPropsWithRef } from "react";

import { useUiLocalization } from "../../localization";
import styles from "./Spinner.module.css";

export type SpinnerSize = "sm" | "md" | "lg";

export interface SpinnerProps extends ComponentPropsWithRef<"span"> {
  size?: SpinnerSize;
  label?: string;
}

export const Spinner = forwardRef<HTMLSpanElement, SpinnerProps>(function Spinner(
  { className, label, size = "md", ...props },
  ref,
) {
  const localized = useUiLocalization();
  const resolvedLabel = label ?? localized.messages.spinner.loading;
  const classes = [styles.root, styles[`size${capitalize(size)}`], className].filter(Boolean).join(" ");

  return (
    <span {...props} ref={ref} className={classes} role="status" aria-label={resolvedLabel} aria-live="polite">
      <span className={styles.circle} aria-hidden="true" />
    </span>
  );
});

function capitalize(value: string): string {
  return value.charAt(0).toUpperCase() + value.slice(1);
}
