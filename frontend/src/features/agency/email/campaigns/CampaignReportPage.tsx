import { useParams } from 'react-router-dom';
import { ErrorState } from '@/components/ui/ErrorState';
import { PageHeader } from '@/components/ui/PageHeader';
import { SkeletonText } from '@/components/ui/Skeleton';
import { useCampaignReport } from '../api/queries';
import { CampaignReportView } from '../reports/CampaignReportView';
import { CampaignStatusBadge } from '../shared/ui';

export function CampaignReportPage({ channel = 'email' }: { channel?: 'email' | 'sms' }) {
  const { id } = useParams();
  const report = useCampaignReport(id, channel);
  const base = channel === 'sms' ? '/agency/sms' : '/agency/email/campaigns';
  return (
    <>
      <PageHeader
        title={report.data ? `${report.data.name} — report` : 'Campaign report'}
        breadcrumbs={[
          { label: channel === 'sms' ? 'SMS & WhatsApp' : 'Campaigns', to: base },
          { label: report.data?.name ?? 'Campaign', to: `${base}/${id}` },
          { label: 'Report' },
        ]}
        meta={report.data && <CampaignStatusBadge status={report.data.status} />}
        description={report.data?.subject ?? undefined}
      />
      {report.isError ? (
        <ErrorState error={report.error} onRetry={() => void report.refetch()} />
      ) : report.data ? (
        <CampaignReportView report={report.data} />
      ) : (
        <SkeletonText lines={8} />
      )}
    </>
  );
}
