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
import { useSiteCopy } from './site/copy';
import './LandingPage.css';

/** Icons for the editable steps and rules (by position; extra items reuse them in order). */
const STEP_ICONS = [Megaphone, Upload, Wallet];
const RULE_ICONS = [UserCheck, Hash, Eye, CalendarClock];

/** Marketing home page for prospective participants. */
export function LandingPage() {
  const { hash } = useLocation();
  const copy = useSiteCopy();
  const browserTitle = copy.text('creators.seo.title');

  useEffect(() => {
    document.title = browserTitle;
  }, [browserTitle]);

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
              {copy.text('creators.hero.eyebrow')}
            </p>
            <h1 id="hero-title" className="landing-hero__title">
              {copy.text('creators.hero.title')} <span className="landing-hero__accent">{copy.text('creators.hero.titleAccent')}</span>
            </h1>
            <p className="landing-hero__lead">{copy.text('creators.hero.lead')}</p>
            <div className="landing-hero__ctas">
              <ButtonLink to="/register" variant="highlight" size="lg" trailingIcon={<ArrowRight />}>
                {copy.text('creators.hero.primaryCta')}
              </ButtonLink>
              <ButtonLink to="/#how-it-works" variant="secondary" size="lg">
                {copy.text('creators.hero.secondaryCta')}
              </ButtonLink>
            </div>
            <ul className="landing-hero__trust">
              {copy.list('creators.hero.trust').map((item) => (
                <li key={item}>
                  <CheckCircle2 aria-hidden="true" /> {item}
                </li>
              ))}
            </ul>
          </div>

          <div className="landing-hero__visual" aria-hidden="true">
            <div className="hero-card hero-card--main">
              <div className="hero-card__row">
                <span className="hero-card__label">Next payout</span>
                <span className="hero-card__pill">Scheduled</span>
              </div>
              <p className="hero-card__amount">{copy.text('creators.hero.payoutSchedule')}</p>
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
            <p className="eyebrow">{copy.text('creators.how.eyebrow')}</p>
            <h2 id="how-title" className="landing-section__title">
              {copy.text('creators.how.title')}
            </h2>
          </div>
          <ol className="landing-steps">
            {copy.pairs('creators.how.steps').map(({ title, text }, index) => {
              const Icon = STEP_ICONS[index % STEP_ICONS.length];
              return (
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
              );
            })}
          </ol>
        </div>
      </section>

      <section id="rules" tabIndex={-1} className="landing-section landing-section--alt" aria-labelledby="rules-title">
        <div className="container">
          <div className="landing-section__header">
            <p className="eyebrow">{copy.text('creators.rules.eyebrow')}</p>
            <h2 id="rules-title" className="landing-section__title">
              {copy.text('creators.rules.title')}
            </h2>
            <p className="landing-section__lead">{copy.text('creators.rules.lead')}</p>
          </div>
          <ul className="landing-rules">
            {copy.pairs('creators.rules.items').map(({ title, text }, index) => {
              const Icon = RULE_ICONS[index % RULE_ICONS.length];
              return (
              <li key={title} className="landing-rule">
                <span className="landing-rule__icon" aria-hidden="true">
                  <Icon />
                </span>
                <h3 className="landing-rule__title">{title}</h3>
                <p className="landing-rule__text">{text}</p>
              </li>
              );
            })}
          </ul>
        </div>
      </section>

      <section className="landing-section" aria-labelledby="faq-title">
        <div className="container landing-faq">
          <div className="landing-section__header landing-faq__header">
            <p className="eyebrow">{copy.text('creators.faq.eyebrow')}</p>
            <h2 id="faq-title" className="landing-section__title">
              {copy.text('creators.faq.title')}
            </h2>
            <p className="landing-section__lead">{copy.text('creators.faq.lead')}</p>
            <ButtonLink to="/faq" variant="secondary" trailingIcon={<ArrowRight />}>
              {copy.text('creators.faq.cta')}
            </ButtonLink>
          </div>
          <dl className="landing-faq__list">
            {copy.pairs('creators.faq.items').map((item) => (
              <div key={item.title} className="landing-faq__item">
                <dt>{item.title}</dt>
                <dd>{item.text}</dd>
              </div>
            ))}
          </dl>
        </div>
      </section>

      <section className="landing-cta" aria-labelledby="cta-title">
        <div className="container landing-cta__inner">
          <div>
            <h2 id="cta-title" className="landing-cta__title">
              {copy.text('creators.cta.title')}
            </h2>
            <p className="landing-cta__text">{copy.text('creators.cta.text')}</p>
          </div>
          <ButtonLink to="/register" variant="highlight" size="lg" trailingIcon={<ArrowRight />}>
            {copy.text('creators.cta.button')}
          </ButtonLink>
        </div>
      </section>
    </div>
  );
}
