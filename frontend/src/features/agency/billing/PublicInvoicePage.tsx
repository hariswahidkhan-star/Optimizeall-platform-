import { useQuery } from '@tanstack/react-query';
import { Download } from 'lucide-react';
import { useEffect, useState } from 'react';
import { useParams } from 'react-router-dom';
import { Button, EmptyState, ErrorState, Skeleton, useToast } from '@/components/ui';
import { api } from '@/lib/api/client';
import { isApiError } from '@/lib/api/errors';
import type { PublicInvoice } from './api/types';
import { InvoiceDocumentView } from './components/InvoiceDocumentView';
import { billingErrorMessage } from './lib';

/** Tokens are 43 URL-safe base64 characters; anything else is not sent to the API. */
export const PUBLIC_TOKEN = /^[A-Za-z0-9_-]{43}$/;

/** Public, tokenized invoice view (`/i/:token`) — linked from invoice emails; no sign-in needed. */
export function PublicInvoicePage() {
  const { token = '' } = useParams();
  const toast = useToast();
  const [downloading, setDownloading] = useState(false);
  const valid = PUBLIC_TOKEN.test(token);
  const query = useQuery({
    queryKey: ['public-invoice', token],
    queryFn: ({ signal }) => api.get<PublicInvoice>(`/public/invoices/${token}`, { signal }),
    enabled: valid,
    retry: false,
  });

  useEffect(() => {
    if (query.data) document.title = `Invoice ${query.data.number} · ${query.data.payment.companyName}`;
  }, [query.data]);

  const notFound = !valid || (isApiError(query.error) && query.error.status === 404);
  return (
    <div className="container stack bill-public">
      {notFound ? (
        <EmptyState headingLevel={1} title="This invoice link isn’t valid" description="Check the link in your email, or ask us to send the invoice again." />
      ) : query.isError ? (
        <ErrorState headingLevel={1} error={query.error} onRetry={() => void query.refetch()} />
      ) : !query.data ? (
        <Skeleton height="30rem" />
      ) : (
        <InvoiceDocumentView
          invoice={query.data}
          actions={
            <Button
              variant="secondary"
              leadingIcon={<Download />}
              loading={downloading}
              onClick={async () => {
                setDownloading(true);
                try {
                  await api.download(`/public/invoices/${token}/document`, `invoice-${query.data?.number ?? ''}.html`);
                } catch (error) {
                  toast.error('Download failed', billingErrorMessage(error));
                } finally {
                  setDownloading(false);
                }
              }}
            >
              Download
            </Button>
          }
        />
      )}
    </div>
  );
}
