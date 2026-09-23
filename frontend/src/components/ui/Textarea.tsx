import clsx from 'clsx';
import { forwardRef, type TextareaHTMLAttributes } from 'react';
import { useFieldControlProps } from './fieldContext';
import './forms.css';

export interface TextareaProps extends TextareaHTMLAttributes<HTMLTextAreaElement> {
  invalid?: boolean;
}

export const Textarea = forwardRef<HTMLTextAreaElement, TextareaProps>(function Textarea(
  { className, rows = 4, ...props },
  ref,
) {
  const controlProps = useFieldControlProps(props);
  return <textarea ref={ref} rows={rows} className={clsx('ui-textarea', className)} {...controlProps} />;
});
