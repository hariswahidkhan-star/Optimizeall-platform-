import { useSearchParams } from 'react-router-dom';
import { PageHeader } from '@/components/ui/PageHeader';
import { Tabs } from '@/components/ui/Tabs';
import { AnnouncementsTab } from './AnnouncementsTab';
import { BannersTab } from './BannersTab';
import { CopyEditor } from './CopyEditor';
import { EmailTemplatesTab } from './EmailTemplatesTab';
import { FaqsTab } from './FaqsTab';
import { OnboardingTab } from './OnboardingTab';

const TABS = ['banners', 'announcements', 'faqs', 'onboarding', 'copy', 'emails'] as const;

export function ContentPage() {
  const [params, setParams] = useSearchParams();
  const current = TABS.find((t) => t === params.get('tab')) ?? 'banners';

  return (
    <>
      <PageHeader
        title="Content"
        description="Homepage banners, announcements, FAQs, onboarding steps, portal texts and email templates. Changes go live as soon as they’re saved and are recorded in the audit log."
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
          {
            id: 'copy',
            label: 'Portal texts',
            content: (
              <CopyEditor
                endpoint="/admin/content/copy"
                title="Portal texts"
                description="Headings and messages of the help centre and the creator home page. Website page texts are edited in Agency → Website → Page texts."
              />
            ),
          },
          { id: 'emails', label: 'Email templates', content: <EmailTemplatesTab /> },
        ]}
      />
    </>
  );
}
