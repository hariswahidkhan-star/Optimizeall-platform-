import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useState } from 'react';
import { describe, expect, it } from 'vitest';
import { json, makeUser, mockFetch, session } from '@/test/fetchMock';
import { axeViolations, renderWithApp } from '@/test/render';
import type { RateAssignment, RateCard, RateExplanation, PersonRates, RateGroup } from './api/types';
import { RateCardDetailPage } from './cards/RateCardDetailPage';
import { RateCardsPage } from './cards/RateCardsPage';
import {
  emptyRates,
  RateLinesEditor,
  ratesHints,
  ratesToInput,
  type RatesForm,
} from './components/RateLinesEditor';
import { RateGroupDetailPage } from './groups/RateGroupDetailPage';
import { PersonRatesSection } from './person/PersonRatesSection';

const PERMS = [
  'campaigns.view',
  'campaigns.manage',
  'users.view',
  'rates.view',
  'rates.manage',
  'rates.assign',
];
const paged = <T,>(items: T[]) => ({ items, total: items.length, page: 1, pageSize: 25, totalPages: 1 });

function sessionFor(permissions = PERMS, id = 'u1') {
  return () => json(200, session(makeUser({ id, roles: ['CampaignManager'], permissions, timeZone: 'UTC' })));
}

function makeCard(overrides: Partial<RateCard> = {}): RateCard {
  return {
    id: 'card1',
    name: 'Micro creators',
    description: '10k–50k followers',
    kind: 'Standard',
    status: 'Active',
    currency: 'USD',
    currentVersion: 1,
    owner: null,
    createdAt: '2026-09-01T00:00:00Z',
    updatedAt: '2026-09-01T00:00:00Z',
    archivedAt: null,
    archiveReason: null,
    concurrencyStamp: 'stamp1',
    versions: [
      {
        id: 'v1',
        version: 1,
        currency: 'USD',
        dailyCapPerParticipant: null,
        weeklyCapPerParticipant: null,
        campaignCapPerParticipant: null,
        stackCampaignBonuses: true,
        effectiveFrom: '2026-09-01T00:00:00Z',
        createdAt: '2026-09-01T00:00:00Z',
        createdBy: { id: 'u1', displayName: 'Maya' },
        reason: 'Initial rates',
        status: 'Approved',
        decidedBy: null,
        decidedAt: null,
        decisionNote: null,
        maxIncreasePercent: null,
        isCurrent: true,
        lines: [
          { id: 'l1', platform: null, format: null, countryCode: null, amount: 10, label: null },
          {
            id: 'l2',
            platform: 'Instagram',
            format: 'ShortVideo',
            countryCode: null,
            amount: 14,
            label: 'Reel fee',
          },
        ],
      },
    ],
    assignments: [],
    usedBySubmissions: 3,
    fourEyesRequiredAbovePercent: true,
    fourEyesThresholdPercent: 50,
    ...overrides,
  };
}

function makeAssignment(overrides: Partial<RateAssignment> = {}): RateAssignment {
  return {
    id: 'a1',
    level: 'GlobalGroup',
    levelLabel: 'Group rate (all campaigns)',
    target: 'Group',
    card: {
      id: 'card1',
      name: 'Micro creators',
      kind: 'Standard',
      status: 'Active',
      currency: 'USD',
      currentVersion: 1,
    },
    person: null,
    group: { id: 'g1', name: 'Micro influencers', membershipMode: 'Manual', priority: 30 },
    campaign: null,
    isCustom: false,
    validFrom: null,
    validTo: null,
    endedAt: null,
    endReason: null,
    note: 'Assigned',
    isActive: true,
    createdAt: '2026-09-01T00:00:00Z',
    createdBy: { id: 'u1', displayName: 'Maya' },
    concurrencyStamp: 's1',
    ...overrides,
  };
}

describe('RateLinesEditor', () => {
  function Harness({ onValue }: { onValue: (v: RatesForm) => void }) {
    const [value, setValue] = useState<RatesForm>(emptyRates('USD'));
    return (
      <RateLinesEditor
        value={value}
        onChange={(v) => {
          setValue(v);
          onValue(v);
        }}
      />
    );
  }

  it('edits rates per platform and format and converts them to the API shape', async () => {
    const user = userEvent.setup();
    mockFetch({
      'GET /meta/currencies': () =>
        json(200, [
          { code: 'USD', minorUnits: 2 },
          { code: 'EUR', minorUnits: 2 },
          { code: 'JPY', minorUnits: 0 },
        ]),
    });
    let last: RatesForm = emptyRates();
    const { container } = renderWithApp(<Harness onValue={(v) => (last = v)} />, { withAuth: false });
    await user.type(screen.getByLabelText(/Rate 1 amount/), '10');
    await user.click(screen.getByRole('button', { name: 'Add rate' }));
    await user.selectOptions(screen.getByLabelText(/Rate 2 platform/), 'Instagram');
    await user.selectOptions(screen.getByLabelText(/Rate 2 format/), 'ShortVideo');
    await user.type(screen.getByLabelText(/Rate 2 amount/), '14.5');
    await user.type(screen.getByLabelText(/Rate 2 country/), 'pk');
    expect(ratesToInput(last).lines).toEqual([
      { platform: null, format: null, countryCode: null, amount: 10, label: null },
      { platform: 'Instagram', format: 'ShortVideo', countryCode: 'PK', amount: 14.5, label: null },
    ]);
    expect(ratesHints(last)).toEqual([]);
    expect(await axeViolations(container)).toEqual([]);
  });

  it('hints duplicate conditions and missing amounts', () => {
    const form = emptyRates();
    form.lines = [
      { key: 'a', platform: 'TikTok', format: '', countryCode: '', amount: '5', label: '' },
      { key: 'b', platform: 'TikTok', format: '', countryCode: '', amount: '', label: '' },
    ];
    const hints = ratesHints(form);
    expect(hints.some((h) => h.includes('same platform'))).toBe(true);
    expect(hints.some((h) => h.includes('needs an amount'))).toBe(true);
  });
});

describe('RateCardsPage', () => {
  it('lists cards and creates a new one', async () => {
    const user = userEvent.setup();
    const { calls } = mockFetch({
      'POST /auth/refresh': sessionFor(),
      'GET /meta/currencies': () =>
        json(200, [
          { code: 'USD', minorUnits: 2 },
          { code: 'EUR', minorUnits: 2 },
        ]),
      'GET /admin/rate-cards': () =>
        json(
          200,
          paged([
            {
              id: 'card1',
              name: 'Micro creators',
              description: null,
              kind: 'Standard',
              status: 'Active',
              currency: 'USD',
              currentVersion: 2,
              lineCount: 3,
              minAmount: 10,
              maxAmount: 14,
              activeAssignments: 4,
              pendingApproval: true,
              updatedAt: '2026-09-01T00:00:00Z',
            },
          ]),
        ),
      'POST /admin/rate-cards': () => json(201, makeCard({ id: 'card2', name: 'Nano creators' })),
      'GET /admin/rate-cards/card2': () => json(200, makeCard({ id: 'card2', name: 'Nano creators' })),
    });
    renderWithApp(<RateCardsPage />, {
      route: '/manage/rate-cards',
      path: '/manage/rate-cards',
      routes: [{ path: '/manage/rate-cards/:cardId', element: <p>Card page</p> }],
    });
    const table = await screen.findByRole('table', { name: 'Rate cards' });
    expect(within(table).getByRole('link', { name: 'Micro creators' })).toHaveAttribute(
      'href',
      '/manage/rate-cards/card1',
    );
    expect(within(table).getByText('Awaiting approval')).toBeInTheDocument();

    await user.click(screen.getAllByRole('button', { name: 'New rate card' })[0]!);
    const dialog = await screen.findByRole('dialog', { name: 'New rate card' });
    await user.type(within(dialog).getByLabelText(/^Name/), 'Nano creators');
    await user.type(within(dialog).getByLabelText(/Rate 1 amount/), '8');
    await user.type(within(dialog).getByLabelText(/^Reason/), 'New nano tier');
    await user.click(within(dialog).getByRole('button', { name: 'Create rate card' }));
    await screen.findByText('Card page');
    const post = calls.find((c) => c.method === 'POST' && c.path === '/admin/rate-cards')!;
    expect(post.body).toMatchObject({
      name: 'Nano creators',
      reason: 'New nano tier',
      activate: true,
      currency: 'USD',
      lines: [{ amount: 8, platform: null, format: null }],
    });
  });
});

describe('RateCardDetailPage', () => {
  const pending = {
    ...makeCard().versions[0]!,
    id: 'v2',
    version: 2,
    status: 'PendingApproval' as const,
    isCurrent: false,
    maxIncreasePercent: 80,
    reason: 'Q4 raise',
    createdBy: { id: 'u9', displayName: 'Other Manager' },
  };

  it('shows a pending raise and lets a different person approve it', async () => {
    const user = userEvent.setup();
    const card = makeCard({ versions: [pending, makeCard().versions[0]!] });
    const { calls } = mockFetch({
      'POST /auth/refresh': sessionFor(),
      'GET /admin/rate-cards/card1': () => json(200, card),
      'POST /admin/rate-cards/card1/versions/2/approve': () => json(200, card),
    });
    const { container } = renderWithApp(<RateCardDetailPage />, {
      route: '/manage/rate-cards/card1',
      path: '/manage/rate-cards/:cardId',
    });
    expect(await screen.findByRole('heading', { level: 1, name: 'Micro creators' })).toBeInTheDocument();
    expect(screen.getByText(/raises rates by up to 80%/)).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Approve' }));
    await waitFor(() =>
      expect(calls.some((c) => c.path === '/admin/rate-cards/card1/versions/2/approve')).toBe(true),
    );
    expect(await axeViolations(container)).toEqual([]);
  });

  it('does not let the author approve their own raise', async () => {
    const card = makeCard({
      versions: [{ ...pending, createdBy: { id: 'u1', displayName: 'Me' } }, makeCard().versions[0]!],
    });
    mockFetch({
      'POST /auth/refresh': sessionFor(),
      'GET /admin/rate-cards/card1': () => json(200, card),
    });
    renderWithApp(<RateCardDetailPage />, {
      route: '/manage/rate-cards/card1',
      path: '/manage/rate-cards/:cardId',
    });
    expect(await screen.findByRole('button', { name: 'Approve' })).toBeDisabled();
    expect(screen.getByRole('button', { name: /New version/ })).toBeDisabled();
  });
});

describe('RateGroupDetailPage', () => {
  it('removes several selected members at once with a reason', async () => {
    const user = userEvent.setup();
    const group: RateGroup = {
      id: 'g1',
      name: 'Micro influencers',
      description: null,
      priority: 30,
      membershipMode: 'Manual',
      autoTiers: [],
      autoMinFollowers: null,
      autoMaxFollowers: null,
      autoRequireVerified: true,
      autoRule: null,
      memberCount: 2,
      createdAt: '2026-09-01T00:00:00Z',
      updatedAt: '2026-09-01T00:00:00Z',
      archivedAt: null,
      archiveReason: null,
      concurrencyStamp: 'gs',
      assignments: [makeAssignment()],
    };
    const member = (id: string, name: string) => ({
      userId: id,
      displayName: name,
      email: `${id}@example.test`,
      countryCode: 'PK',
      tier: 'Gold',
      status: 'Active',
      isTestAccount: false,
      addedAt: '2026-09-01T00:00:00Z',
      addedBy: null,
      note: null,
      followers: null,
    });
    const { calls } = mockFetch({
      'POST /auth/refresh': sessionFor(),
      'GET /admin/rate-groups/g1': () => json(200, group),
      'GET /admin/rate-groups/g1/members': () =>
        json(200, paged([member('p1', 'Sara Khan'), member('p2', 'Omar Ali')])),
      'POST /admin/rate-groups/g1/members/remove': () =>
        json(200, { requested: 2, added: 0, unchanged: 0, removed: 2, rejected: [], warnings: [] }),
    });
    renderWithApp(<RateGroupDetailPage />, {
      route: '/manage/rate-groups/g1',
      path: '/manage/rate-groups/:groupId',
    });
    const table = await screen.findByRole('table', { name: 'Group members' });
    await user.click(await within(table).findByRole('checkbox', { name: /Sara Khan/ }));
    await user.click(within(table).getByRole('checkbox', { name: /Omar Ali/ }));
    await user.click(screen.getByRole('button', { name: 'Remove 2 from group' }));
    const confirm = await screen.findByRole('alertdialog');
    await user.type(within(confirm).getByLabelText(/Reason/), 'Moved to Macro');
    await user.click(within(confirm).getByRole('button', { name: 'Remove' }));
    await waitFor(() =>
      expect(calls.some((c) => c.path === '/admin/rate-groups/g1/members/remove')).toBe(true),
    );
    const post = calls.find((c) => c.path === '/admin/rate-groups/g1/members/remove')!;
    expect(post.body).toEqual({ userIds: ['p1', 'p2'], reason: 'Moved to Macro' });
  });
});

describe('PersonRatesSection', () => {
  it('shows the effective rate and explains which rule won', async () => {
    const user = userEvent.setup();
    const rates: PersonRates = {
      user: { id: 'p1', displayName: 'Sara Khan' },
      countryCode: 'PK',
      tier: 'Gold',
      status: 'Active',
      isTestAccount: false,
      campaign: null,
      groups: [
        {
          id: 'g1',
          name: 'Micro influencers',
          membershipMode: 'Manual',
          priority: 30,
          addedAt: '2026-09-01T00:00:00Z',
          matchedPlatforms: [],
        },
      ],
      assignments: [makeAssignment()],
      effective: [
        {
          platform: 'Instagram',
          format: null,
          level: 'GlobalPersonalCustom',
          levelLabel: 'Custom rate (all campaigns)',
          sourceLabel: 'Custom rate v1',
          amount: 11,
          currency: 'USD',
          assignmentId: 'a9',
          rateCardId: 'c9',
          rateCardVersion: 1,
          validTo: '2026-09-30T00:00:00Z',
        },
      ],
      precedence: [],
    };
    const explanation: RateExplanation = {
      platform: 'Instagram',
      format: null,
      countryCode: 'PK',
      tier: 'Gold',
      followers: 18400,
      evaluatedAt: '2026-09-24T00:00:00Z',
      campaign: null,
      campaignPolicy: null,
      maxMultiplier: null,
      winner: 'GlobalPersonalCustom',
      summary: 'Custom rate (all campaigns) wins: 11 USD.',
      candidates: [
        {
          level: 'GlobalPersonalCustom',
          levelLabel: 'Custom rate (all campaigns)',
          outcome: 'Won',
          reason: 'highest precedence',
          assignmentId: 'a9',
          rateCardId: 'c9',
          cardName: 'Custom rate — Sara Khan',
          version: 1,
          groupId: null,
          groupName: null,
          priority: 0,
          lineId: 'l9',
          lineConditions: 'Any post',
          amount: 11,
          currency: 'USD',
          validFrom: null,
          validTo: '2026-09-30T00:00:00Z',
        },
        {
          level: 'GlobalGroup',
          levelLabel: 'Group rate (all campaigns)',
          outcome: 'Outranked',
          reason: 'Group rate (all campaigns) ranks below Custom rate (all campaigns)',
          assignmentId: 'a1',
          rateCardId: 'card1',
          cardName: 'Micro creators',
          version: 1,
          groupId: 'g1',
          groupName: 'Micro influencers',
          priority: 30,
          lineId: 'l1',
          lineConditions: 'Any post',
          amount: 10,
          currency: 'USD',
          validFrom: null,
          validTo: null,
        },
      ],
      conversion: null,
      conversionError: null,
      quote: null,
      precedence: [
        { rank: 4, level: 'GlobalPersonalCustom', label: 'Custom rate (all campaigns)' },
        { rank: 6, level: 'GlobalGroup', label: 'Group rate (all campaigns)' },
      ],
    };
    mockFetch({
      'POST /auth/refresh': sessionFor(),
      'GET /campaigns/options': () => json(200, []),
      'GET /admin/users/p1/rates': () => json(200, rates),
      'GET /admin/users/p1/rates/explain': () => json(200, explanation),
    });
    renderWithApp(<PersonRatesSection userId="p1" displayName="Sara Khan" />);
    const table = await screen.findByRole('table', { name: 'Effective rates of Sara Khan' });
    expect(within(table).getByText('Custom rate · all campaigns')).toBeInTheDocument();
    expect(screen.getAllByRole('link', { name: 'Micro influencers' }).length).toBeGreaterThan(0);
    await user.click(within(table).getByRole('button', { name: 'Explain' }));
    const dialog = await screen.findByRole('dialog', { name: 'Explain this rate' });
    expect(await within(dialog).findByText(/wins: 11 USD/)).toBeInTheDocument();
    const candidates = within(dialog).getByRole('table', { name: 'Candidate rates' });
    expect(within(candidates).getByText('Applies')).toBeInTheDocument();
    expect(within(candidates).getByText('Outranked')).toBeInTheDocument();
    expect(within(candidates).getByText(/via Micro influencers/)).toBeInTheDocument();
  });
});
