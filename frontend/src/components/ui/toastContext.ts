import { createContext, useContext, type ReactNode } from 'react';

export type ToastTone = 'success' | 'danger' | 'info' | 'warning';

export interface ToastInput {
  title: ReactNode;
  description?: ReactNode;
  tone?: ToastTone;
  /** Auto-dismiss after ms (default 5000; 0 keeps it until dismissed). */
  duration?: number;
}

export interface ToastApi {
  show: (toast: ToastInput) => string;
  success: (title: ReactNode, description?: ReactNode) => string;
  error: (title: ReactNode, description?: ReactNode) => string;
  info: (title: ReactNode, description?: ReactNode) => string;
  dismiss: (id: string) => void;
}

export const ToastContext = createContext<ToastApi | null>(null);

export function useToast(): ToastApi {
  const value = useContext(ToastContext);
  if (!value) throw new Error('useToast must be used inside <ToastProvider>.');
  return value;
}
