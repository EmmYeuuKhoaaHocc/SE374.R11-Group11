import { forwardRef, useId, type ComponentPropsWithRef, type ReactNode } from "react";

import styles from "./Card.module.css";

export interface CardProps extends Omit<ComponentPropsWithRef<"article">, "children" | "title"> {
  heading?: ReactNode;
  description?: ReactNode;
  header?: ReactNode;
  footer?: ReactNode;
  children: ReactNode;
}

export const Card = forwardRef<HTMLElement, CardProps>(function Card({
  "aria-labelledby": ariaLabelledby,
  children,
  className,
  description,
  footer,
  header,
  heading,
  ...props
}: CardProps, ref) {
  const headingId = useId();
  const classes = [styles.root, className].filter(Boolean).join(" ");

  return (
    <article {...props} ref={ref} className={classes} aria-labelledby={heading ? headingId : ariaLabelledby}>
      {header ? <div className={styles.header}>{header}</div> : null}
      {heading || description ? (
        <div className={styles.headingBlock}>
          {heading ? <h2 className={styles.heading} id={headingId}>{heading}</h2> : null}
          {description ? <p className={styles.description}>{description}</p> : null}
        </div>
      ) : null}
      <div className={styles.content}>{children}</div>
      {footer ? <div className={styles.footer}>{footer}</div> : null}
    </article>
  );
});
