import { Link } from 'react-router-dom';
import { Card, CardBody, CardHeader } from '@/components/ui/Card';
import { useMyCertificates } from '@/features/learning/api';
import { BadgeImage } from '@/features/learning/components/CourseCard';
import '@/features/learning/learning.css';

/** Course badges earned by the participant (profile page). Only valid certificates are shown. */
export function EarnedBadges() {
  const q = useMyCertificates();
  const valid = q.data?.filter((c) => !c.revoked) ?? [];
  if (valid.length === 0) return null;
  return (
    <Card as="section" aria-labelledby="earned-badges-heading">
      <CardHeader
        title="Earned badges"
        titleId="earned-badges-heading"
        description="Certificates from the Optimize All Academy. Each links to its public verification page."
      />
      <CardBody>
        <ul className="lx-home-rings">
          {valid.map((c) => (
            <li key={c.id}>
              <BadgeImage src={c.links.badgeImageUrl} size={56} />
              <span>
                <Link to={`/verify/certificates/${c.id}`}>{c.badgeName}</Link>
                <br />
                <span className="lx-muted">{c.courseTitle}</span>
              </span>
            </li>
          ))}
        </ul>
      </CardBody>
    </Card>
  );
}
