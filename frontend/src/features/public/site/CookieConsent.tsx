import { Cookie } from 'lucide-react';
import { useEffect, useId, useState } from 'react';
import { Link } from 'react-router-dom';
import { Button, Switch } from '@/components/ui';
import { type AnalyticsIds, applyConsent, hasAnyTag, saveConsent, useConsent } from './consent';

/**
 * Cookie consent banner. Shown only when the site actually has analytics or marketing tags configured (without them the
 * site sets necessary cookies only). Nothing but necessary storage is used until the visitor chooses; accepted tags are
 * then loaded by {@link applyConsent}. "Cookie settings" in the footer reopens it.
 */
export function CookieConsent({ ids, open, onClose }: { ids: AnalyticsIds | null | undefined; open: boolean; onClose: () => void }) {
  const choice = useConsent();
  const [customize, setCustomize] = useState(false);
  const [analytics, setAnalytics] = useState(choice?.analytics ?? false);
  const [marketing, setMarketing] = useState(choice?.marketing ?? false);
  const titleId = useId();

  useEffect(() => {
    applyConsent(choice, ids);
  }, [choice, ids]);

  useEffect(() => {
    if (open) {
      setAnalytics(choice?.analytics ?? false);
      setMarketing(choice?.marketing ?? false);
      setCustomize(true);
    }
  }, [open, choice]);

  const visible = open || (hasAnyTag(ids) && !choice);
  if (!visible) return null;

  const decide = (value: { analytics: boolean; marketing: boolean }) => {
    saveConsent(value);
    setCustomize(false);
    onClose();
  };

  return (
    <section className="site-consent" aria-labelledby={titleId}>
      <div className="site-consent__head">
        <Cookie aria-hidden="true" />
        <h2 id={titleId}>Your privacy choices</h2>
      </div>
      <p>
        We use necessary cookies to run this site. With your permission we'd also like to use analytics cookies to
        understand how it's used, and marketing cookies to measure our advertising. See our{' '}
        <Link to="/cookie-policy">cookie policy</Link>.
      </p>
      {customize && (
        <div className="site-consent__options">
          <Switch checked disabled onCheckedChange={() => undefined} label="Necessary" description="Security and remembering your choices. Always on." />
          <Switch checked={analytics} onCheckedChange={setAnalytics} label="Analytics" description="Google Analytics 4: anonymous usage statistics." />
          <Switch checked={marketing} onCheckedChange={setMarketing} label="Marketing" description="Google Tag Manager and Meta Pixel: ad measurement." />
        </div>
      )}
      <div className="site-consent__actions">
        {customize ? (
          <Button size="sm" onClick={() => decide({ analytics, marketing })}>
            Save choices
          </Button>
        ) : (
          <Button size="sm" variant="secondary" onClick={() => setCustomize(true)}>
            Customize
          </Button>
        )}
        <Button size="sm" variant="secondary" onClick={() => decide({ analytics: false, marketing: false })}>
          Reject all
        </Button>
        <Button size="sm" onClick={() => decide({ analytics: true, marketing: true })}>
          Accept all
        </Button>
      </div>
    </section>
  );
}
