/** Sign-in URL after a forced sign-out: shows "Your session has expired" and returns to `next` afterwards. */
export function loginPathAfterExpiry(next: string): string {
  return `/login?expired=1&next=${encodeURIComponent(next)}`;
}

/** Where "Exit" (or the end of an impersonation session) takes the staff member: the admin users page. */
export const IMPERSONATION_EXIT_PATH = '/admin/users';
