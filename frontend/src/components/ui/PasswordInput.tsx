import { Eye, EyeOff } from 'lucide-react';
import { forwardRef, useState } from 'react';
import { IconButton } from './IconButton';
import { Input, type InputProps } from './Input';

export type PasswordInputProps = Omit<InputProps, 'type' | 'trailing'>;

/** Password field with a show/hide toggle. */
export const PasswordInput = forwardRef<HTMLInputElement, PasswordInputProps>(
  function PasswordInput(props, ref) {
    const [visible, setVisible] = useState(false);
    return (
      <Input
        ref={ref}
        type={visible ? 'text' : 'password'}
        spellCheck={false}
        autoCapitalize="off"
        {...props}
        trailing={
          <IconButton
            size="sm"
            label={visible ? 'Hide password' : 'Show password'}
            aria-pressed={visible}
            icon={visible ? <EyeOff /> : <Eye />}
            onClick={() => setVisible((v) => !v)}
          />
        }
      />
    );
  },
);
