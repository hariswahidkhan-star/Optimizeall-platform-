import { Badge, Card, CardBody, CardHeader, CopyField, DateTime, KeyValueList } from '@/components/ui';
import type { Requirements, SocialPlatform } from '../api/types';

function Tokens({ value, empty }: { value: string | null; empty: string }) {
  const tokens = (value ?? '').split(/[\s,]+/).filter(Boolean);
  if (tokens.length === 0) return <span className="text-muted">{empty}</span>;
  return (
    <ul className="rv-chips">
      {tokens.map((token) => (
        <li key={token}>
          <Badge tone="brand" size="sm">
            {token}
          </Badge>
        </li>
      ))}
    </ul>
  );
}

/** Left column of the workspace: what the participant was asked to post. */
export function RequirementsPanel({
  requirements,
  platform,
  countryCode,
}: {
  requirements: Requirements;
  platform: SocialPlatform;
  countryCode: string;
}) {
  const r = requirements;
  return (
    <Card as="section" aria-labelledby="rv-req-heading">
      <CardHeader
        title="Campaign requirements"
        titleId="rv-req-heading"
        description={r.campaignTitle}
        headingLevel={2}
      />
      <CardBody className="stack">
        <div>
          <h3 className="rv-subheading">Posting instructions</h3>
          <p className="rv-prewrap">{r.postingInstructions || '—'}</p>
        </div>
        <div>
          <h3 className="rv-subheading">Required hashtags</h3>
          <Tokens value={r.requiredHashtags} empty="None required" />
        </div>
        <div>
          <h3 className="rv-subheading">Required mentions</h3>
          <Tokens value={r.requiredMentions} empty="None required" />
        </div>
        <div>
          <h3 className="rv-subheading">
            Disclosure for {platform} · {countryCode}
          </h3>
          <CopyField label="Required disclosure text" hideLabel value={r.disclosureText} />
        </div>
        <KeyValueList
          layout="inline"
          items={[
            {
              label: 'Platforms',
              value: (
                <ul className="rv-chips">
                  {r.allowedPlatforms.map((p) => (
                    <li key={p}>
                      <Badge size="sm" tone={p === platform ? 'brand' : 'neutral'}>
                        {p}
                      </Badge>
                    </li>
                  ))}
                </ul>
              ),
            },
            {
              label: 'Campaign window',
              value: (
                <>
                  <DateTime value={r.startsAt} format="date" /> – <DateTime value={r.endsAt} format="date" />
                </>
              ),
            },
            { label: 'Submission deadline', value: <DateTime value={r.submissionDeadline} /> },
            {
              label: 'Must stay live',
              value: r.minPostLiveHours > 0 ? `${r.minPostLiveHours} hours after posting` : 'No minimum',
            },
            { label: 'Screenshot', value: r.requireScreenshot ? 'Required' : 'Optional' },
          ]}
        />
      </CardBody>
    </Card>
  );
}
