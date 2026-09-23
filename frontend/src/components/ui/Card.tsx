import clsx from 'clsx';
import type { HTMLAttributes, ReactNode } from 'react';
import './Card.css';

export interface CardProps extends HTMLAttributes<HTMLElement> {
  /** Render as a <section> (with a heading inside) or <article>; defaults to <div>. */
  as?: 'div' | 'section' | 'article' | 'li';
  flat?: boolean;
  /** Hover elevation; add a link with className "ui-card__link" inside to make the whole card clickable. */
  interactive?: boolean;
}

export function Card({ as: Tag = 'div', flat, interactive, className, ...rest }: CardProps) {
  return (
    <Tag
      className={clsx('ui-card', flat && 'ui-card--flat', interactive && 'ui-card--interactive', className)}
      {...rest}
    />
  );
}

export interface CardHeaderProps {
  title: ReactNode;
  description?: ReactNode;
  actions?: ReactNode;
  headingLevel?: 2 | 3 | 4;
  /** id for the heading, e.g. to label a <section aria-labelledby>. */
  titleId?: string;
  className?: string;
}

export function CardHeader({
  title,
  description,
  actions,
  headingLevel = 2,
  titleId,
  className,
}: CardHeaderProps) {
  const Heading = `h${headingLevel}` as const;
  return (
    <div className={clsx('ui-card__header', className)}>
      <div className="ui-card__heading">
        <Heading id={titleId} className="ui-card__title">
          {title}
        </Heading>
        {description && <p className="ui-card__description">{description}</p>}
      </div>
      {actions && <div className="ui-card__actions">{actions}</div>}
    </div>
  );
}

export function CardBody({
  flush,
  className,
  ...rest
}: HTMLAttributes<HTMLDivElement> & { flush?: boolean }) {
  return <div className={clsx('ui-card__body', flush && 'ui-card__body--flush', className)} {...rest} />;
}

export function CardFooter({ className, ...rest }: HTMLAttributes<HTMLDivElement>) {
  return <div className={clsx('ui-card__footer', className)} {...rest} />;
}
