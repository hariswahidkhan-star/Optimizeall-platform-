const GOOGLE_AUTHORIZE_PREFIX = 'https://accounts.google.com/';

/** Full-page navigation to Google's consent screen. Only Google's own origin is accepted. */
export function redirectToGoogle(url: string): void {
  if (!url.startsWith(GOOGLE_AUTHORIZE_PREFIX)) throw new Error('Unexpected sign-in URL.');
  window.location.assign(url);
}
