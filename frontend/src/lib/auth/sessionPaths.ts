/** Sign-in URL after a forced sign-out: shows "Your session has expired" and returns to `next` afterwards. */
export function loginPathAfterExpiry(next: string): string {
  return `/login?expired=1&next=${encodeURIComponent(next)}`;
}
