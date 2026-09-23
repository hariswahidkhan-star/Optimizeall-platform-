import {
  ArrowRight,
  BadgeCheck,
  CalendarClock,
  CheckCircle2,
  Eye,
  FileCheck2,
  Hash,
  Megaphone,
  ShieldCheck,
  Upload,
  UserCheck,
  Wallet,
} from 'lucide-react';
import { useEffect } from 'react';
import { useLocation } from 'react-router-dom';
import { ButtonLink } from '@/components/ui/ButtonLink';
import './LandingPage.css';

const STEPS = [
  {
    icon: Megaphone,
    title: 'Pick a campaign',
    text: 'Browse campaigns from companies that match your audience. Each one comes with approved content and clear rules.',
  },
  {
    icon: Upload,
    title: 'Share and submit proof',
    text: 'Post the content from your established account, then submit the post link and a screenshot.',
  },
  {
    icon: Wallet,
    title: 'Get paid for approved posts',
    text: 'A reviewer checks every post. Approved earnings are paid out on a predictable biweekly schedule.',
  },
];

const RULES = [
  {
    icon: UserCheck,
    title: 'Established accounts only',
    text: 'Real accounts with a genuine history and audience. We verify every connected account — no bots, no bought followers.',
  },
  {
    icon: Hash,
    title: 'Disclosure is required',
    text: 'Every paid post is clearly labelled (for example #ad or the platform’s paid-partnership tag). Your audience always knows.',
  },
  {
    icon: Eye,
    title: 'Human review',
    text: 'People, not just algorithms, review each submission. If something needs fixing you get specific feedback and a chance to correct it.',
  },
  {
    icon: CalendarClock,
    title: 'Transparent biweekly payouts',
    text: 'See exactly what is pending, approved and scheduled. Payouts run every two weeks with a full history of every amount.',
  },
];

const FAQ_TEASER = [
  {
    q: 'Who can join?',
    a: 'Anyone with an established, genuine social account in a supported country. New or inactive accounts may not qualify for every campaign.',
  },
  {
    q: 'How much can I earn?',
    a: 'Each campaign shows its reward per approved post up front, so you know what you’ll earn before you share.',
  },
  {
    q: 'When do I get paid?',
    a: 'Approved earnings are included in the next biweekly payout once you reach the minimum payout amount.',
  },
];

/** Marketing home page for prospective participants. */
export function LandingPage() {
  const { hash } = useLocation();

  useEffect(() => {
    document.title = 'Optimize All — Get paid to share brands you believe in';
  }, []);

  // Router links to /#section: scroll to the section and move focus there for keyboard users.
  useEffect(() => {
    if (!hash) return;
    const target = document.getElementById(hash.slice(1));
    if (target) {
      target.scrollIntoView({ block: 'start' });
      target.focus({ preventScroll: true });
    }
  }, [hash]);

  return (
    <div className="landing">
      <section className="landing-hero" aria-labelledby="hero-title">
        <div className="container landing-hero__inner">
          <div className="landing-hero__copy">
            <p className="landing-hero__eyebrow">
              <span className="landing-hero__dot" aria-hidden="true" />
              Paid sharing campaigns, done right
            </p>
            <h1 id="hero-title" className="landing-hero__title">
              Get paid to share brands <span className="landing-hero__accent">you believe in</span>
            </h1>
            <p className="landing-hero__lead">
              Optimize All connects creators and everyday influencers with companies that want authentic reach. Share
              approved content from your own established accounts, submit proof, and get paid for every approved
              post.
            </p>
            <div className="landing-hero__ctas">
              <ButtonLink to="/register" variant="highlight" size="lg" trailingIcon={<ArrowRight />}>
                Create your free account
              </ButtonLink>
              <ButtonLink to="/#how-it-works" variant="secondary" size="lg">
                See how it works
              </ButtonLink>
            </div>
            <ul className="landing-hero__trust">
              <li>
                <CheckCircle2 aria-hidden="true" /> Free to join
              </li>
              <li>
                <CheckCircle2 aria-hidden="true" /> Every post reviewed by a person
              </li>
              <li>
                <CheckCircle2 aria-hidden="true" /> Paid every two weeks
              </li>
            </ul>
          </div>

          <div className="landing-hero__visual" aria-hidden="true">
            <div className="hero-card hero-card--main">
              <div className="hero-card__row">
                <span className="hero-card__label">Next payout</span>
                <span className="hero-card__pill">Scheduled</span>
              </div>
              <p className="hero-card__amount">Every other Friday</p>
              <div className="hero-card__bar">
                <span style={{ width: '72%' }} />
              </div>
              <ul className="hero-card__list">
                <li>
                  <FileCheck2 /> Post submitted <span>Proof received</span>
                </li>
                <li>
                  <BadgeCheck /> Reviewed <span>Approved</span>
                </li>
                <li>
                  <Wallet /> Earnings <span>Added to payout</span>
                </li>
              </ul>
            </div>
            <div className="hero-card hero-card--float">
              <ShieldCheck />
              <span>
                <strong>Disclosure included</strong>
                <span>#ad added to caption</span>
              </span>
            </div>
            <div className="hero-rings" />
          </div>
        </div>
      </section>

      <section id="how-it-works" tabIndex={-1} className="landing-section" aria-labelledby="how-title">
        <div className="container">
          <div className="landing-section__header">
            <p className="eyebrow">How it works</p>
            <h2 id="how-title" className="landing-section__title">
              Three steps from post to payout
            </h2>
          </div>
          <ol className="landing-steps">
            {STEPS.map(({ icon: Icon, title, text }, index) => (
              <li key={title} className="landing-step">
                <span className="landing-step__number" aria-hidden="true">
                  {index + 1}
                </span>
                <span className="landing-step__icon" aria-hidden="true">
                  <Icon />
                </span>
                <h3 className="landing-step__title">
                  <span className="visually-hidden">Step {index + 1}: </span>
                  {title}
                </h3>
                <p className="landing-step__text">{text}</p>
              </li>
            ))}
          </ol>
        </div>
      </section>

      <section id="rules" tabIndex={-1} className="landing-section landing-section--alt" aria-labelledby="rules-title">
        <div className="container">
          <div className="landing-section__header">
            <p className="eyebrow">Trust & rules</p>
            <h2 id="rules-title" className="landing-section__title">
              Fair for you, honest with your audience
            </h2>
            <p className="landing-section__lead">
              The rules protect your reputation and the companies you work with. They are the same for everyone.
            </p>
          </div>
          <ul className="landing-rules">
            {RULES.map(({ icon: Icon, title, text }) => (
              <li key={title} className="landing-rule">
                <span className="landing-rule__icon" aria-hidden="true">
                  <Icon />
                </span>
                <h3 className="landing-rule__title">{title}</h3>
                <p className="landing-rule__text">{text}</p>
              </li>
            ))}
          </ul>
        </div>
      </section>

      <section className="landing-section" aria-labelledby="faq-title">
        <div className="container landing-faq">
          <div className="landing-section__header landing-faq__header">
            <p className="eyebrow">Questions</p>
            <h2 id="faq-title" className="landing-section__title">
              Good to know
            </h2>
            <p className="landing-section__lead">Quick answers to what people ask most.</p>
            <ButtonLink to="/faq" variant="secondary" trailingIcon={<ArrowRight />}>
              Read all FAQs
            </ButtonLink>
          </div>
          <dl className="landing-faq__list">
            {FAQ_TEASER.map((item) => (
              <div key={item.q} className="landing-faq__item">
                <dt>{item.q}</dt>
                <dd>{item.a}</dd>
              </div>
            ))}
          </dl>
        </div>
      </section>

      <section className="landing-cta" aria-labelledby="cta-title">
        <div className="container landing-cta__inner">
          <div>
            <h2 id="cta-title" className="landing-cta__title">
              Ready to earn from the brands you already love?
            </h2>
            <p className="landing-cta__text">It takes two minutes to create an account and connect your first profile.</p>
          </div>
          <ButtonLink to="/register" variant="highlight" size="lg" trailingIcon={<ArrowRight />}>
            Get started
          </ButtonLink>
        </div>
      </section>
    </div>
  );
}
