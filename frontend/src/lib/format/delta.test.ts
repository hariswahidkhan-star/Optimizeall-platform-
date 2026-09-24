import { metricTrend, periodDelta, previousRange } from './delta';

describe('periodDelta', () => {
  const count = (value: number | null) => ({ key: 'postsSubmitted', value, unit: 'count' });

  it('compares counts as a relative change against the same key', () => {
    expect(periodDelta(count(150), [count(100)], 'vs previous 30 days')).toMatchObject({
      value: 0.5,
      label: 'vs previous 30 days',
      positiveIsGood: true,
      neutral: false,
    });
  });

  it('compares percentages in points and marks spend as neutral and costs as lower-is-better', () => {
    const rate = { key: 'approvalRate', value: 80, unit: 'percent' };
    expect(periodDelta(rate, [{ ...rate, value: 75.5 }], 'x')).toMatchObject({
      value: 4.5,
      display: '+4.5 pts',
    });
    const spend = { key: 'spend', value: 20, unit: 'money', currency: 'USD' };
    expect(periodDelta(spend, [{ ...spend, value: 10 }], 'x')?.neutral).toBe(true);
    const cost = { key: 'costPerApprovedPost', value: 5, unit: 'money', currency: 'USD' };
    expect(periodDelta(cost, [{ ...cost, value: 10 }], 'x')?.positiveIsGood).toBe(false);
  });

  it('never compares different currencies, missing values or a zero baseline', () => {
    const usd = { key: 'spend', value: 20, unit: 'money', currency: 'USD' };
    expect(periodDelta(usd, [{ ...usd, currency: 'PKR' }], 'x')).toBeUndefined();
    expect(periodDelta(count(null), [count(10)], 'x')).toBeUndefined();
    expect(periodDelta(count(10), [count(0)], 'x')).toBeUndefined();
    expect(periodDelta(count(10), undefined, 'x')).toBeUndefined();
  });
});

describe('previousRange', () => {
  it('returns the same-length window that ends the day before', () => {
    expect(previousRange('2026-09-01', '2026-09-30')).toEqual({ from: '2026-08-02', to: '2026-08-31' });
  });
});

describe('metricTrend', () => {
  it('maps count metrics to their daily series with a spoken summary', () => {
    const series = [
      { date: 'a', registrations: 1, submissions: 2, approvals: 1, clicks: 0 },
      { date: 'b', registrations: 3, submissions: 5, approvals: 4, clicks: 9 },
    ];
    expect(metricTrend('postsApproved', 'Approved', series)).toEqual({
      values: [1, 4],
      label: 'Approved per day over the period, from 1 to 4; peak 4',
    });
    expect(metricTrend('spend', 'Spend', series)).toBeUndefined();
  });
});
