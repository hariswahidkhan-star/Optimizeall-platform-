import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { api } from '@/lib/api/client';
import type {
  Automation,
  AutomationListItem,
  Campaign,
  CampaignListItem,
  CampaignReport,
  Checklist,
  EmailList,
  ListHealth,
  MessageChannel,
  Overview,
  PagedResult,
  Segment,
  SenderProfile,
  SubscriberDetail,
  SubscriberListItem,
  Suppression,
  Template,
  TemplateListItem,
  Workspace,
  WorkspaceSettings,
} from './types';

/** Base path of the staff API for this area (relative to /api/v1). */
export const EMAIL_API = '/agency/email';

/** Campaign API base for a channel: SMS/WhatsApp campaigns live under /sms/campaigns (sms.manage). */
export function campaignsPath(channel: MessageChannel | 'sms' | 'email' = 'Email'): string {
  return channel === 'Email' || channel === 'email' ? `${EMAIL_API}/campaigns` : `${EMAIL_API}/sms/campaigns`;
}

/** Query keys (all under 'email' so a mutation can invalidate the whole area). */
export const emailKeys = {
  all: ['email'] as const,
  workspaces: () => ['email', 'workspaces'] as const,
  overview: (ws: string) => ['email', ws, 'overview'] as const,
  lists: (ws: string) => ['email', ws, 'lists'] as const,
  list: (id: string) => ['email', 'list', id] as const,
  listHealth: (id: string) => ['email', 'list', id, 'health'] as const,
  subscribers: (ws: string, params: object) => ['email', ws, 'subscribers', params] as const,
  subscriber: (id: string) => ['email', 'subscriber', id] as const,
  segments: (ws: string) => ['email', ws, 'segments'] as const,
  segment: (id: string) => ['email', 'segment', id] as const,
  templates: (ws: string) => ['email', ws, 'templates'] as const,
  template: (id: string) => ['email', 'template', id] as const,
  campaigns: (ws: string, channel: string, params: object) => ['email', ws, 'campaigns', channel, params] as const,
  campaign: (id: string) => ['email', 'campaign', id] as const,
  checklist: (id: string) => ['email', 'campaign', id, 'checklist'] as const,
  report: (id: string) => ['email', 'campaign', id, 'report'] as const,
  automations: (ws: string) => ['email', ws, 'automations'] as const,
  tags: (ws: string) => ['email', ws, 'tags'] as const,
  fields: (ws: string) => ['email', ws, 'fields'] as const,
  automation: (id: string) => ['email', 'automation', id] as const,
  settings: (ws: string) => ['email', ws, 'settings'] as const,
  senders: (ws: string) => ['email', ws, 'senders'] as const,
  suppressions: (ws: string, params: object) => ['email', ws, 'suppressions', params] as const,
};

/** Query parameter for the workspace (omitted for the agency's own workspace). */
export function wsQuery(clientId: string | null | undefined): { clientId?: string } {
  return clientId ? { clientId } : {};
}

export function useWorkspaces() {
  return useQuery({
    queryKey: emailKeys.workspaces(),
    queryFn: ({ signal }) => api.get<Workspace[]>(`${EMAIL_API}/workspaces`, { signal }),
    staleTime: 5 * 60_000,
  });
}

export function useOverview(clientId: string | null) {
  return useQuery({
    queryKey: emailKeys.overview(clientId ?? 'agency'),
    queryFn: ({ signal }) => api.get<Overview>(`${EMAIL_API}/overview`, { query: wsQuery(clientId), signal }),
  });
}

export function useLists(clientId: string | null) {
  return useQuery({
    queryKey: emailKeys.lists(clientId ?? 'agency'),
    queryFn: ({ signal }) => api.get<EmailList[]>(`${EMAIL_API}/lists`, { query: wsQuery(clientId), signal }),
  });
}

export function useList(id: string | undefined) {
  return useQuery({
    queryKey: emailKeys.list(id ?? ''),
    queryFn: ({ signal }) => api.get<EmailList>(`${EMAIL_API}/lists/${id}`, { signal }),
    enabled: !!id,
  });
}

export function useListHealth(id: string | undefined) {
  return useQuery({
    queryKey: emailKeys.listHealth(id ?? ''),
    queryFn: ({ signal }) => api.get<ListHealth>(`${EMAIL_API}/lists/${id}/health`, { query: { days: 90 }, signal }),
    enabled: !!id,
  });
}

export interface SubscriberParams {
  listId?: string;
  status?: string;
  tag?: string;
  search?: string;
  page: number;
  pageSize: number;
}

export function useSubscribers(clientId: string | null, params: SubscriberParams) {
  return useQuery({
    queryKey: emailKeys.subscribers(clientId ?? 'agency', params),
    queryFn: ({ signal }) =>
      api.get<PagedResult<SubscriberListItem>>(`${EMAIL_API}/subscribers`, {
        query: { clientAccountId: clientId ?? undefined, ...params },
        signal,
      }),
    placeholderData: keepPreviousData,
  });
}

export function useSubscriber(id: string | undefined) {
  return useQuery({
    queryKey: emailKeys.subscriber(id ?? ''),
    queryFn: ({ signal }) => api.get<SubscriberDetail>(`${EMAIL_API}/subscribers/${id}`, { signal }),
    enabled: !!id,
  });
}

export function useSegments(clientId: string | null) {
  return useQuery({
    queryKey: emailKeys.segments(clientId ?? 'agency'),
    queryFn: ({ signal }) => api.get<Segment[]>(`${EMAIL_API}/segments`, { query: wsQuery(clientId), signal }),
  });
}

export function useSegment(id: string | undefined) {
  return useQuery({
    queryKey: emailKeys.segment(id ?? ''),
    queryFn: ({ signal }) => api.get<Segment>(`${EMAIL_API}/segments/${id}`, { signal }),
    enabled: !!id,
  });
}

export function useTemplates(clientId: string | null) {
  return useQuery({
    queryKey: emailKeys.templates(clientId ?? 'agency'),
    queryFn: ({ signal }) => api.get<TemplateListItem[]>(`${EMAIL_API}/templates`, { query: wsQuery(clientId), signal }),
  });
}

export function useTemplate(id: string | undefined) {
  return useQuery({
    queryKey: emailKeys.template(id ?? ''),
    queryFn: ({ signal }) => api.get<Template>(`${EMAIL_API}/templates/${id}`, { signal }),
    enabled: !!id,
  });
}

export function useCampaigns(clientId: string | null, channel: 'email' | 'sms', params: { status?: string; search?: string; page: number; pageSize: number }) {
  return useQuery({
    queryKey: emailKeys.campaigns(clientId ?? 'agency', channel, params),
    queryFn: ({ signal }) =>
      api.get<PagedResult<CampaignListItem>>(campaignsPath(channel), {
        query: { clientAccountId: clientId ?? undefined, ...params },
        signal,
      }),
    placeholderData: keepPreviousData,
  });
}

export function useCampaign(id: string | undefined, channel: 'email' | 'sms') {
  return useQuery({
    queryKey: emailKeys.campaign(id ?? ''),
    queryFn: ({ signal }) => api.get<Campaign>(`${campaignsPath(channel)}/${id}`, { signal }),
    enabled: !!id,
  });
}

export function useChecklist(id: string | undefined, channel: 'email' | 'sms') {
  return useQuery({
    queryKey: emailKeys.checklist(id ?? ''),
    queryFn: ({ signal }) => api.get<Checklist>(`${campaignsPath(channel)}/${id}/checklist`, { signal }),
    enabled: !!id,
  });
}

export function useCampaignReport(id: string | undefined, channel: 'email' | 'sms') {
  return useQuery({
    queryKey: emailKeys.report(id ?? ''),
    queryFn: ({ signal }) => api.get<CampaignReport>(`${campaignsPath(channel)}/${id}/report`, { signal }),
    enabled: !!id,
  });
}

export function useAutomations(clientId: string | null, includeArchived = false) {
  return useQuery({
    queryKey: [...emailKeys.automations(clientId ?? 'agency'), includeArchived ? 'all' : 'active'],
    queryFn: ({ signal }) =>
      api.get<AutomationListItem[]>(`${EMAIL_API}/automations${includeArchived ? '/all' : ''}`, { query: wsQuery(clientId), signal }),
  });
}

/** A tag or custom-field key of the workspace with its usage (`GET …/tags`, `GET …/fields`). */
export interface AudienceKey {
  key: string;
  contacts: number;
  referencedBy: number;
}

export function useAudienceKeys(kind: 'tags' | 'fields', clientId: string | null) {
  return useQuery({
    queryKey: kind === 'tags' ? emailKeys.tags(clientId ?? 'agency') : emailKeys.fields(clientId ?? 'agency'),
    queryFn: ({ signal }) => api.get<AudienceKey[]>(`${EMAIL_API}/${kind}`, { query: wsQuery(clientId), signal }),
  });
}

export function useAutomation(id: string | undefined) {
  return useQuery({
    queryKey: emailKeys.automation(id ?? ''),
    queryFn: ({ signal }) => api.get<Automation>(`${EMAIL_API}/automations/${id}`, { signal }),
    enabled: !!id,
  });
}

export function useWorkspaceSettings(clientId: string | null) {
  return useQuery({
    queryKey: emailKeys.settings(clientId ?? 'agency'),
    queryFn: ({ signal }) => api.get<WorkspaceSettings>(`${EMAIL_API}/settings`, { query: wsQuery(clientId), signal }),
  });
}

export function useSenders(clientId: string | null) {
  return useQuery({
    queryKey: emailKeys.senders(clientId ?? 'agency'),
    queryFn: ({ signal }) => api.get<SenderProfile[]>(`${EMAIL_API}/senders`, { query: wsQuery(clientId), signal }),
  });
}

export function useSuppressions(clientId: string | null, params: { search?: string; page: number; pageSize: number }) {
  return useQuery({
    queryKey: emailKeys.suppressions(clientId ?? 'agency', params),
    queryFn: ({ signal }) =>
      api.get<PagedResult<Suppression>>(`${EMAIL_API}/suppressions`, {
        query: { clientAccountId: clientId ?? undefined, ...params },
        signal,
      }),
    placeholderData: keepPreviousData,
  });
}
