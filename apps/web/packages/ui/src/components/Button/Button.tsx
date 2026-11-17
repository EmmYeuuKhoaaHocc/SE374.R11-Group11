import {
  forwardRef,
  type ComponentPropsWithRef,
  type ReactNode,
} from "react";

import styles from "./Button.module.css";

export type ButtonVariant = "primary" | "secondary" | "outline" | "ghost" | "danger";
export type ButtonSize = "sm" | "md" | "lg";

export interface ButtonProps extends ComponentPropsWithRef<"button"> {
  variant?: ButtonVariant;
  size?: ButtonSize;
  isLoading?: boolean;
  startIcon?: ReactNode;
  endIcon?: ReactNode;
}

export const Button = forwardRef<HTMLButtonElement, ButtonProps>(function Button(
  {
    children,
    className,
    endIcon,
    isLoading = false,
    startIcon,
    type,
    variant = "primary",
    size = "md",
    disabled,
    ...props
  },
  ref,
) {
  const classes = [styles.root, styles[variant], styles[`size${capitalize(size)}`], className]
    .filter(Boolean)
    .join(" ");

  return (
    <button
      {...props}
      ref={ref}
      className={classes}
      type={type ?? "button"}
      disabled={disabled || isLoading}
      aria-busy={isLoading || undefined}
    >
      {isLoading ? <span className={styles.loadingIndicator} aria-hidden="true" /> : startIcon}
      <span className={isLoading ? styles.loadingLabel : undefined}>{children}</span>
      {!isLoading && endIcon}
    </button>
  );
});

function capitalize(value: string): string {
  return value.charAt(0).toUpperCase() + value.slice(1);
}
