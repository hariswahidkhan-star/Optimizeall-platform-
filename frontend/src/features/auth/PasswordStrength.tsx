import { Check, Circle } from 'lucide-react';
import { passwordRules, passwordScore, SCORE_LABELS } from './passwordPolicy';
import '@/components/ui/forms.css';

/** Strength meter + live policy checklist. Rendered as the password field's hint (linked via aria-describedby). */
export function PasswordStrength({ password, email }: { password: string; email?: string }) {
  const score = passwordScore(password, email);
  const rules = passwordRules(password, email);
  return (
    <div className="ui-strength" data-score={score}>
      <div className="ui-strength__bars" aria-hidden="true">
        {Array.from({ length: 4 }, (_, i) => (
          <span key={i} className="ui-strength__bar" />
        ))}
      </div>
      {score > 0 && (
        <p className="ui-strength__label">
          <span>
            Password strength: <strong>{SCORE_LABELS[score]}</strong>
          </span>
        </p>
      )}
      <ul className="ui-strength__rules">
        {rules.map((rule) => (
          <li key={rule.id} data-met={rule.met}>
            {rule.met ? <Check aria-hidden="true" /> : <Circle aria-hidden="true" />}
            <span>
              {rule.label}
              <span className="visually-hidden">{rule.met ? ' — done' : ' — not yet'}</span>
            </span>
          </li>
        ))}
      </ul>
    </div>
  );
}
