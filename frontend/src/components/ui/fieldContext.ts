import { createContext, useContext } from 'react';

export interface FieldContextValue {
  id: string;
  describedBy: string | undefined;
  invalid: boolean;
  required: boolean;
}

/** Provided by FormField so controls pick up id, aria-describedby, aria-invalid and required automatically. */
export const FieldContext = createContext<FieldContextValue | null>(null);

export function useFieldContext(): FieldContextValue | null {
  return useContext(FieldContext);
}

interface ControlA11yProps {
  id?: string;
  'aria-describedby'?: string;
  'aria-invalid'?: boolean | 'true' | 'false' | 'grammar' | 'spelling';
  required?: boolean;
}

/** Merges FormField context into a control's own props (explicit props win). */
export function useFieldControlProps<P extends ControlA11yProps>(props: P & { invalid?: boolean }) {
  const field = useFieldContext();
  const { invalid, ...rest } = props;
  const describedBy = [field?.describedBy, props['aria-describedby']].filter(Boolean).join(' ') || undefined;
  const isInvalid = invalid ?? field?.invalid ?? false;
  return {
    ...rest,
    id: props.id ?? field?.id,
    required: props.required ?? field?.required,
    'aria-describedby': describedBy,
    'aria-invalid': isInvalid || undefined,
  } as Omit<P, 'invalid'>;
}
