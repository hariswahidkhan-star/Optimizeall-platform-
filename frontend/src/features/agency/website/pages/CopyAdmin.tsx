import { PageHeader } from '@/components/ui';
import { CopyEditor } from '@/features/admin/content/CopyEditor';
import '@/features/admin/admin.css';

/** Agency → Website → Page texts: headlines, introductions, buttons and lists of every built-in website page. */
export function SiteCopyPage() {
  return (
    <div className="stack">
      <PageHeader
        title="Page texts"
        description="The wording of the built-in website pages — home, services, pricing, industries, case studies, blog, team, careers, the contact and booking forms and the creators page. Navigation, footer links and contact details live in Site settings; content pages in Pages."
      />
      <CopyEditor
        endpoint="/agency/website/copy"
        title="Website texts"
        description="Pick a page, change its texts and save. Reset a text to go back to the original wording. Changes go live immediately and are recorded in the audit log."
      />
    </div>
  );
}
