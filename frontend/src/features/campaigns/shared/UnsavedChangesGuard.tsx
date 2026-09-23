import { useEffect, useRef } from 'react';
import { useBlocker } from 'react-router-dom';
import { ConfirmDialog } from '@/components/ui';

/**
 * Warns before leaving a page with unsaved changes: in-app navigation shows a confirmation dialog, and closing or
 * reloading the tab triggers the browser's own prompt.
 */
export function UnsavedChangesGuard({ when, message }: { when: boolean; message?: string }) {
  const blocker = useBlocker(
    ({ currentLocation, nextLocation }) => when && currentLocation.pathname !== nextLocation.pathname,
  );

  const proceeding = useRef(false);

  useEffect(() => {
    if (blocker.state === 'blocked') proceeding.current = false;
  }, [blocker.state]);

  useEffect(() => {
    if (!when) return;
    const onBeforeUnload = (event: BeforeUnloadEvent) => {
      event.preventDefault();
      event.returnValue = '';
    };
    window.addEventListener('beforeunload', onBeforeUnload);
    return () => window.removeEventListener('beforeunload', onBeforeUnload);
  }, [when]);

  return (
    <ConfirmDialog
      open={blocker.state === 'blocked'}
      onClose={() => {
        if (!proceeding.current) blocker.reset?.();
      }}
      onConfirm={() => {
        proceeding.current = true;
        blocker.proceed?.();
      }}
      title="Discard unsaved changes?"
      description={
        message ?? 'You have changes that have not been saved. Leaving this page will discard them.'
      }
      confirmLabel="Discard and leave"
      cancelLabel="Stay on page"
      tone="danger"
    />
  );
}
