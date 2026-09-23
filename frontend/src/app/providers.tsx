import { QueryClientProvider, type QueryClient } from '@tanstack/react-query';
import { useState, type ReactNode } from 'react';
import { ToastProvider } from '@/components/ui/ToastProvider';
import { createQueryClient } from '@/lib/api/query';
import { ThemeProvider } from '@/lib/theme/ThemeProvider';

/**
 * App-wide providers that do not depend on the router: server state, theme and toasts.
 * AuthProvider lives inside the router (see router.tsx) because it navigates on session expiry.
 */
export function AppProviders({ children, queryClient }: { children: ReactNode; queryClient?: QueryClient }) {
  const [client] = useState(() => queryClient ?? createQueryClient());
  return (
    <QueryClientProvider client={client}>
      <ThemeProvider>
        <ToastProvider>{children}</ToastProvider>
      </ThemeProvider>
    </QueryClientProvider>
  );
}
