import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { Link, useLocation } from 'react-router-dom';
import { Alert } from '@/components/ui/Alert';
import { Button } from '@/components/ui/Button';
import { Card, CardBody, CardHeader } from '@/components/ui/Card';
import { DateTime } from '@/components/ui/DateTime';
import { useToast } from '@/components/ui/toastContext';
import { api } from '@/lib/api/client';
import { isApiError } from '@/lib/api/errors';
import type { SignInMethods } from '@/lib/api/types';
import { signInMethodsQueryKey, startGoogleFlow } from './googleApi';
import { GoogleButton, GoogleLogo } from './GoogleButton';
import './GoogleButton.css';

/**
 * Profile → security: connect or disconnect Google. Disconnecting is refused (by the API, and explained here) when
 * Google is the account's only way to sign in; such users set a password through "Forgot password?" first.
 * Renders nothing when Google sign-in isn't configured and no Google account is connected.
 */
export function GoogleConnectionCard() {
  const location = useLocation();
  const toast = useToast();
  const queryClient = useQueryClient();
  const [starting, setStarting] = useState(false);
  const [startError, setStartError] = useState<string | null>(null);

  const methods = useQuery({
    queryKey: signInMethodsQueryKey,
    queryFn: () => api.get<SignInMethods>('/auth/external-logins'),
  });

  const unlink = useMutation({
    mutationFn: () => api.delete<void>('/auth/external-logins/google'),
    onSuccess: async () => {
      toast.success('Google disconnected', 'You can no longer sign in with Google.');
      await queryClient.invalidateQueries({ queryKey: signInMethodsQueryKey });
    },
  });

  const data = methods.data;
  if (!data) return null;
  const google = data.externalLogins.find((l) => l.provider === 'google');
  if (!google && !data.googleEnabled) return null;

  const connect = async () => {
    setStartError(null);
    setStarting(true);
    try {
      await startGoogleFlow('link', location.pathname);
    } catch (error) {
      setStarting(false);
      setStartError(isApiError(error) ? error.title : 'Couldn’t reach Google. Please try again.');
    }
  };

  const onlyMethod = !!google && !data.hasPassword && data.externalLogins.length === 1;

  return (
    <Card as="section" aria-labelledby="google-connection-title">
      <CardHeader
        titleId="google-connection-title"
        title="Sign in with Google"
        description="Use your Google account to sign in without a password."
      />
      <CardBody className="stack">
        {startError && (
          <Alert tone="danger" role="alert">
            {startError}
          </Alert>
        )}
        {unlink.isError && (
          <Alert tone="danger" role="alert">
            {isApiError(unlink.error) ? unlink.error.title : 'Couldn’t disconnect Google. Please try again.'}
          </Alert>
        )}
        {google ? (
          <div className="google-connection">
            <div className="google-connection__identity">
              <GoogleLogo />
              <div>
                <p>
                  Connected to <strong>{google.email}</strong>
                </p>
                <p className="text-small text-muted">
                  Since <DateTime value={google.createdAt} format="date" />
                </p>
              </div>
            </div>
            <Button
              variant="secondary"
              onClick={() => unlink.mutate()}
              loading={unlink.isPending}
              disabled={onlyMethod}
              aria-describedby={onlyMethod ? 'google-only-method' : undefined}
            >
              Disconnect Google
            </Button>
          </div>
        ) : (
          <div>
            <GoogleButton loading={starting} onClick={connect} />
          </div>
        )}
        {onlyMethod && (
          <p id="google-only-method" className="text-small text-muted">
            Google is currently your only way to sign in. To disconnect it, first set a password with{' '}
            <Link className="ui-link" to="/forgot-password">
              Forgot password?
            </Link>
          </p>
        )}
      </CardBody>
    </Card>
  );
}
