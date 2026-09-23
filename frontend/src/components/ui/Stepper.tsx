import clsx from 'clsx';
import { Check } from 'lucide-react';
import type { ReactNode } from 'react';
import './display.css';

export type StepStatus = 'complete' | 'current' | 'upcoming';

export interface Step {
  id: string;
  title: ReactNode;
  description?: ReactNode;
  status: StepStatus;
  /** Call to action for the current step (e.g. "Connect account"). */
  action?: ReactNode;
}

export interface StepperProps {
  steps: Step[];
  /** Accessible name of the list. */
  label: string;
  className?: string;
}

const STATUS_TEXT: Record<StepStatus, string> = {
  complete: 'Completed',
  current: 'Current step',
  upcoming: 'Not started',
};

/** Vertical checklist (onboarding). Each step announces its status. */
export function Stepper({ steps, label, className }: StepperProps) {
  return (
    <ol className={clsx('ui-stepper', className)} aria-label={label}>
      {steps.map((step, index) => (
        <li
          key={step.id}
          className="ui-stepper__step"
          data-status={step.status}
          aria-current={step.status === 'current' ? 'step' : undefined}
        >
          <span className="ui-stepper__marker" aria-hidden="true">
            {step.status === 'complete' ? <Check /> : index + 1}
          </span>
          <div className="ui-stepper__content">
            <p className="ui-stepper__title">
              {step.title}
              <span className="visually-hidden"> — {STATUS_TEXT[step.status]}</span>
            </p>
            {step.description && <p className="ui-stepper__description">{step.description}</p>}
            {step.action && step.status !== 'complete' && (
              <div className="ui-stepper__action">{step.action}</div>
            )}
          </div>
        </li>
      ))}
    </ol>
  );
}
