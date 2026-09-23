import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useCallback } from 'react';
import { useNavigate } from 'react-router-dom';
import { isApiError } from '@/lib/api/errors';
import { useAuth } from '@/lib/auth/useAuth';
import { reviewApi, reviewKeys } from '../api/reviewApi';
import type { Claim } from '../api/types';
import { parseQueueParams } from '../queueParams';

/** Router state carried from the queue into the workspace so "next" follows the same filters. */
export interface WorkspaceState {
  queueSearch?: string;
}

export function workspacePath(id: string): string {
  return `/review/queue/${id}`;
}

/** Claim (or extend the claim on) a submission. Refreshes the queue and the submission afterwards. */
export function useClaim() {
  const queryClient = useQueryClient();
  return useMutation<Claim, unknown, string>({
    mutationFn: (id) => reviewApi.claim(id),
    onSettled: (_data, _error, id) => {
      void queryClient.invalidateQueries({ queryKey: reviewKeys.queueRoot() });
      void queryClient.invalidateQueries({ queryKey: reviewKeys.detail(id) });
      void queryClient.invalidateQueries({ queryKey: reviewKeys.stats() });
    },
  });
}

export function useRelease() {
  const queryClient = useQueryClient();
  return useMutation<void, unknown, string>({
    mutationFn: (id) => reviewApi.release(id),
    onSettled: (_data, _error, id) => {
      void queryClient.invalidateQueries({ queryKey: reviewKeys.queueRoot() });
      void queryClient.invalidateQueries({ queryKey: reviewKeys.detail(id) });
      void queryClient.invalidateQueries({ queryKey: reviewKeys.stats() });
    },
  });
}

/**
 * Finds, claims and opens the next open submission in the queue (same filters as the queue the reviewer came from).
 * Items held by someone else are skipped; if a claim races with another reviewer the next candidate is tried.
 * Resolves to false when nothing is left.
 */
export function useOpenNext() {
  const navigate = useNavigate();
  const { user } = useAuth();
  return useCallback(
    async (currentId: string, queueSearch = ''): Promise<boolean> => {
      const filters = { ...parseQueueParams(new URLSearchParams(queueSearch)), page: 1, pageSize: 20 };
      const page = await reviewApi.queue(filters);
      const candidates = page.items.filter(
        (item) => item.id !== currentId && (!item.claimedBy || item.claimedBy.id === user?.id),
      );
      for (const candidate of candidates) {
        try {
          await reviewApi.claim(candidate.id);
          navigate(workspacePath(candidate.id), { state: { queueSearch } satisfies WorkspaceState });
          return true;
        } catch (error) {
          if (isApiError(error) && error.status === 409) continue;
          throw error;
        }
      }
      return false;
    },
    [navigate, user?.id],
  );
}
