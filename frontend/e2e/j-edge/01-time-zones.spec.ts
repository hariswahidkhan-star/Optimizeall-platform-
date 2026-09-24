import { expect, test } from '@playwright/test';
import { adminAt, calendarLabel, isoInDays, runId, staff, watchErrors } from './support/edge';

/**
 * Calendar dates (C# DateOnly: blackout days, due dates, report periods) name the same day in every time zone. The
 * browser runs at the extremes of the offset range and in a 45-minute zone; a date parsed as an instant (UTC midnight
 * or noon) would show the previous day at UTC-12 or the next day at UTC+14.
 */
const zones = ['Pacific/Kiritimati', 'Etc/GMT+12', 'Asia/Kathmandu'];

zones.forEach((timezoneId, index) => {
  test.describe(`browser in ${timezoneId}`, () => {
    test.use({ timezoneId, locale: 'en-US' });

    test('booking blackout days show the calendar day that was saved', async ({ page }) => {
      const errors = watchErrors(page);
      const admin = await staff('admin');
      const reason = `Edge blackout ${runId()} ${timezoneId}`;
      // Far ahead and spread out: one blackout per day is allowed, and the Demo seed blocks days in the coming weeks.
      const date = isoInDays(400 + index * 10 + Math.floor(Math.random() * 10));
      const created = await admin.post<{ id: string }>('/agency/website/bookings/blackouts', {
        date,
        reason,
      });
      try {
        await adminAt(page, '/agency/website/bookings');
        const item = page.getByRole('listitem').filter({ hasText: reason });
        await expect(item).toContainText(calendarLabel(date));
        // The browser clock really is in the zone (the page is not just running in UTC).
        const offset = await page.evaluate(() => new Date('2026-06-01T00:00:00Z').getTimezoneOffset());
        expect(offset).toBe(
          { 'Pacific/Kiritimati': -840, 'Etc/GMT+12': 720, 'Asia/Kathmandu': -345 }[timezoneId],
        );
      } finally {
        await admin.delete(`/agency/website/bookings/blackouts/${created.id}`);
      }
      errors.expectClean(`bookings in ${timezoneId}`);
    });
  });
});
