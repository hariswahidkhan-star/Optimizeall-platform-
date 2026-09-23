import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useEffect } from 'react';
import { useParams } from 'react-router-dom';
import { EmptyState, ErrorState, Skeleton } from '@/components/ui';
import { PUBLIC_TOKEN } from '@/features/agency/billing/PublicInvoicePage';
import { api } from '@/lib/api/client';
import { isApiError } from '@/lib/api/errors';
import type { AcceptProposalResponse, PublicProposal } from './api/types';
import { AcceptProposalPanel } from './components/AcceptProposalPanel';
import { ProposalDocumentView } from './components/ProposalDocumentView';
import '@/features/agency/billing/billing.css';

/** Public proposal page (`/p/:token`): read, print, accept with a typed signature, or decline. No sign-in needed. */
export function PublicProposalPage() {
  const { token = '' } = useParams();
  const qc = useQueryClient();
  const valid = PUBLIC_TOKEN.test(token);
  const key = ['public-proposal', token];
  const query = useQuery({
    queryKey: key,
    queryFn: ({ signal }) => api.get<PublicProposal>(`/public/proposals/${token}`, { signal }),
    enabled: valid,
    retry: false,
    // Each load counts as a view; don't refetch on focus.
    refetchOnWindowFocus: false,
    staleTime: Infinity,
  });

  useEffect(() => {
    if (query.data) document.title = `${query.data.title} · ${query.data.agencyName}`;
  }, [query.data]);

  const notFound = !valid || (isApiError(query.error) && query.error.status === 404);
  return (
    <div className="container stack bill-public">
      {notFound ? (
        <EmptyState title="This proposal link isn’t valid" description="Check the link in your email, or ask us to send the proposal again." />
      ) : query.isError ? (
        <ErrorState error={query.error} onRetry={() => void query.refetch()} />
      ) : !query.data ? (
        <Skeleton height="30rem" />
      ) : (
        <>
          <ProposalDocumentView
            version={query.data.version}
            number={query.data.number}
            agencyName={query.data.agencyName}
            preparedFor={query.data.preparedFor ?? query.data.recipientName}
          />
          <div className="bill-document bill-no-print">
            <AcceptProposalPanel
              proposal={query.data}
              askEmail
              onAccept={async (body) => {
                const result = await api.post<AcceptProposalResponse>(`/public/proposals/${token}/accept`, body);
                qc.setQueryData(key, result.proposal);
                return result;
              }}
              onDecline={async (body) => {
                const result = await api.post<PublicProposal>(`/public/proposals/${token}/decline`, body);
                qc.setQueryData(key, result);
              }}
            />
          </div>
        </>
      )}
    </div>
  );
}
