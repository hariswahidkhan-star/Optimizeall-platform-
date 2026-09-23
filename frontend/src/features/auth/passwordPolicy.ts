/**
 * Client-side mirror of the backend PasswordPolicy (Modules/Auth/PasswordPolicy.cs) for instant feedback.
 * The server remains authoritative; its `errors.password` messages are shown when it disagrees.
 */
export const MIN_PASSWORD_LENGTH = 10;

const COMMON = new Set(
  [
    'password123',
    'password1234',
    '1234567890',
    '12345678910',
    'qwertyuiop',
    'qwerty12345',
    'iloveyou123',
    'admin12345',
    'welcome123',
    'letmein123',
    'passw0rd123',
    'abc1234567',
    '0987654321',
    '1q2w3e4r5t',
    'football123',
    'monkey12345',
    'sunshine123',
    'princess123',
    'password!1',
    'optimizeall',
  ].map((p) => p.toLowerCase()),
);

export interface PasswordRule {
  id: 'length' | 'common' | 'email' | 'variety';
  label: string;
  met: boolean;
}

export function passwordRules(password: string, email = ''): PasswordRule[] {
  const local = email.split('@')[0] ?? '';
  const containsEmail = local.length >= 4 && password.toLowerCase().includes(local.toLowerCase());
  return [
    {
      id: 'length',
      label: `At least ${MIN_PASSWORD_LENGTH} characters`,
      met: password.length >= MIN_PASSWORD_LENGTH,
    },
    {
      id: 'common',
      label: 'Not a common password',
      met: password.length > 0 && !COMMON.has(password.toLowerCase()),
    },
    { id: 'email', label: 'Doesn’t contain your email address', met: password.length > 0 && !containsEmail },
    { id: 'variety', label: 'Not repetitive (4+ different characters)', met: new Set(password).size >= 4 },
  ];
}

/** 0 (empty) … 4 (strong). Length and character variety raise the score once the policy is met. */
export function passwordScore(password: string, email = ''): 0 | 1 | 2 | 3 | 4 {
  if (!password) return 0;
  const rules = passwordRules(password, email);
  if (!rules.every((r) => r.met)) return 1;
  const classes = [/[a-z]/, /[A-Z]/, /\d/, /[^A-Za-z0-9]/].filter((re) => re.test(password)).length;
  if (password.length >= 16 || (password.length >= 12 && classes >= 3)) return 4;
  if (password.length >= 12 || classes >= 3) return 3;
  return 2;
}

export const SCORE_LABELS = ['', 'Too weak', 'Fair', 'Good', 'Strong'] as const;

/** First unmet rule as an error message, or null. */
export function passwordProblem(password: string, email = ''): string | null {
  if (!password) return 'Enter a password.';
  const unmet = passwordRules(password, email).find((r) => !r.met);
  if (!unmet) return null;
  switch (unmet.id) {
    case 'length':
      return `Use at least ${MIN_PASSWORD_LENGTH} characters.`;
    case 'common':
      return 'This password is too common.';
    case 'email':
      return 'Don’t include your email address in your password.';
    default:
      return 'Use a less repetitive password.';
  }
}
