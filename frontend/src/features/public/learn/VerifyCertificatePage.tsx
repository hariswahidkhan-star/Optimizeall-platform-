import { BadgeCheck, CheckCircle2, Download, Share2, XCircle } from 'lucide-react';
import { Link, useParams } from 'react-router-dom';
import { Card, CardBody } from '@/components/ui/Card';
import { DateTime } from '@/components/ui/DateTime';
import { ErrorState } from '@/components/ui/ErrorState';
import { Skeleton } from '@/components/ui/Skeleton';
import { useCertificateVerification } from '@/features/learning/api';
import { BadgeImage } from '@/features/learning/components/CourseCard';
import '@/features/learning/learning.css';
import { useDocumentHead } from '../site/head';

/**
 * Public certificate verification (/verify/certificates/:id). In production the web server serves the API's
 * server-rendered page at this address (Open Graph tags for LinkedIn and crawlers); this SPA page renders the same
 * verification for in-app navigation and local development.
 */
export function VerifyCertificatePage() {
  const { id = '' } = useParams();
  const q = useCertificateVerification(id);
  const v = q.data;
  useDocumentHead(
    v
      ? { title: v.seo.title, description: v.seo.description, canonical: v.seo.canonicalPath, noIndex: v.seo.noIndex, jsonLd: v.jsonLd }
      : { title: 'Certificate verification', noIndex: true },
  );
  return (
    <div className="container lx-public lx-public--narrow">
      {q.isPending && <Skeleton height={360} radius="var(--radius-xl)" />}
      {q.isError && (
        <Card flat>
          <ErrorState
            error={q.error}
            headingLevel={1}
            title="We couldn’t find this certificate"
            action={<Link to="/learn">Explore free courses</Link>}
          />
        </Card>
      )}
      {v && (
        <Card as="article" aria-labelledby="verify-title" className="lx-verify">
          <CardBody>
            <p className={v.isValid ? 'lx-verify__status lx-verify__status--ok' : 'lx-verify__status lx-verify__status--bad'} role="status" data-testid="verify-status">
              {v.isValid ? <CheckCircle2 aria-hidden="true" /> : <XCircle aria-hidden="true" />}
              {v.isValid ? 'Valid certificate' : 'Revoked — this certificate is no longer valid'}
            </p>
            <div className="lx-verify__grid">
              <BadgeImage src={v.links.badgeImageUrl} alt={`${v.badgeName} badge`} size={180} />
              <div>
                <p className="lx-cert-mini__kicker">Certificate of achievement</p>
                <h1 id="verify-title" className="lx-verify__name">
                  {v.holderName}
                </h1>
                <p className="lx-verify__lead">
                  earned the <strong>{v.badgeName}</strong> credential for completing{' '}
                  <Link to={`/learn/${v.courseSlug}`}>{v.courseTitle}</Link> and passing its final assessment.
                </p>
                <dl className="lx-verify__facts">
                  <div>
                    <dt>Issued by</dt>
                    <dd>{v.issuerName}</dd>
                  </div>
                  <div>
                    <dt>Issue date</dt>
                    <dd>
                      <DateTime value={v.issuedAt} format="date" />
                    </dd>
                  </div>
                  <div>
                    <dt>Credential ID</dt>
                    <dd>
                      <code>{v.verificationCode}</code>
                    </dd>
                  </div>
                  {v.revokedAt && (
                    <div>
                      <dt>Revoked on</dt>
                      <dd>
                        <DateTime value={v.revokedAt} format="date" />
                      </dd>
                    </div>
                  )}
                </dl>
                {v.skills.length > 0 && (
                  <>
                    <h2 className="lx-subhead">Skills</h2>
                    <ul className="lx-skills">
                      {v.skills.map((s) => (
                        <li key={s}>{s}</li>
                      ))}
                    </ul>
                  </>
                )}
                {v.badgeDescription && (
                  <>
                    <h2 className="lx-subhead">What the holder demonstrated</h2>
                    <p>{v.badgeDescription}</p>
                  </>
                )}
                {v.isValid && (
                  <div className="lx-actions">
                    <a className="ui-button ui-button--primary ui-button--md" href={v.links.pdfUrl} download>
                      <Download aria-hidden="true" /> Download certificate (PDF)
                    </a>
                    <a className="ui-button ui-button--secondary ui-button--md" href={v.links.linkedInShareUrl} target="_blank" rel="noopener noreferrer">
                      <Share2 aria-hidden="true" /> Share on LinkedIn<span className="visually-hidden"> (opens in a new tab)</span>
                    </a>
                    <a className="ui-button ui-button--ghost ui-button--md" href={v.links.openBadgeAssertionUrl} target="_blank" rel="noopener noreferrer">
                      <BadgeCheck aria-hidden="true" /> Open Badge (JSON)<span className="visually-hidden"> (opens in a new tab)</span>
                    </a>
                  </div>
                )}
              </div>
            </div>
          </CardBody>
        </Card>
      )}
    </div>
  );
}
