import { useSearchParams } from 'react-router-dom';
import { PageHeader } from '@/components/ui/PageHeader';
import { Tabs } from '@/components/ui/Tabs';
import { AnnouncementsTab } from './AnnouncementsTab';
import { BannersTab } from './BannersTab';
import { FaqsTab } from './FaqsTab';
import { OnboardingTab } from './OnboardingTab';

const TABS = ['banners', 'announcements', 'faqs', 'onboarding'] as const;

export function ContentPage() {
  const [params, setParams] = useSearchParams();
  const current = TABS.find((t) => t === params.get('tab')) ?? 'banners';

  return (
    <>
      <PageHeader
        title="Content"
        description="Homepage banners, announcements, FAQs and onboarding steps. Changes go live as soon as they’re saved and are recorded in the audit log."
      />
      <Tabs
        label="Content types"
        value={current}
        onValueChange={(tab) => setParams({ tab }, { replace: true })}
        tabs={[
          { id: 'banners', label: 'Banners', content: <BannersTab /> },
          { id: 'announcements', label: 'Announcements', content: <AnnouncementsTab /> },
          { id: 'faqs', label: 'FAQs', content: <FaqsTab /> },
          { id: 'onboarding', label: 'Onboarding steps', content: <OnboardingTab /> },
        ]}
      />
    </>
  );
}
