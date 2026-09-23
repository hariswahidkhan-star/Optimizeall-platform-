import { useQuery } from '@tanstack/react-query';
import { api } from '@/lib/api/client';
import type { PagedResult } from '@/lib/api/types';
import type {
  AgencyDashboard,
  ClientSummary,
  ProjectSummary,
  StaffPerson,
  TimeEntry,
} from '../shared/deliveryTypes';

/** React Query keys of the delivery area (invalidate by prefix). */
export const dk = {
  all: ['delivery'] as const,
  dashboard: ['delivery', 'dashboard'] as const,
  clients: (q?: unknown) => ['delivery', 'clients', q] as const,
  client: (id: string) => ['delivery', 'client', id] as const,
  clientPart: (id: string, part: string) => ['delivery', 'client', id, part] as const,
  projects: (q?: unknown) => ['delivery', 'projects', q] as const,
  project: (id: string) => ['delivery', 'project', id] as const,
  tasks: (projectId: string) => ['delivery', 'project', projectId, 'tasks'] as const,
  task: (id: string) => ['delivery', 'task', id] as const,
  myTasks: (filter: string) => ['delivery', 'my-tasks', filter] as const,
  deliverables: (q?: unknown) => ['delivery', 'deliverables', q] as const,
  deliverable: (id: string) => ['delivery', 'deliverable', id] as const,
  timer: ['delivery', 'timer'] as const,
  week: (date: string, userId?: string) => ['delivery', 'week', date, userId] as const,
  reports: (q?: unknown) => ['delivery', 'reports', q] as const,
  report: (id: string) => ['delivery', 'report', id] as const,
  staff: ['delivery', 'staff'] as const,
  templates: ['delivery', 'templates'] as const,
};

export function useStaff() {
  return useQuery({
    queryKey: dk.staff,
    queryFn: ({ signal }) => api.get<StaffPerson[]>('/agency/staff', { signal }),
    staleTime: 5 * 60_000,
  });
}

export function useClientOptions() {
  return useQuery({
    queryKey: dk.clients({ options: true }),
    queryFn: ({ signal }) =>
      api.get<PagedResult<ClientSummary>>('/agency/clients', { query: { pageSize: 200, sort: 'name', desc: false }, signal }),
    select: (r) => r.items,
    staleTime: 60_000,
  });
}

export function useProjectOptions(clientId?: string, enabled = true) {
  return useQuery({
    queryKey: dk.projects({ options: true, clientId }),
    queryFn: ({ signal }) =>
      api.get<PagedResult<ProjectSummary>>('/agency/projects', { query: { pageSize: 200, clientId }, signal }),
    select: (r) => r.items.filter((p) => p.status !== 'Cancelled'),
    enabled,
    staleTime: 60_000,
  });
}

export function useDashboard(enabled: boolean) {
  return useQuery({
    queryKey: dk.dashboard,
    queryFn: ({ signal }) => api.get<AgencyDashboard>('/agency/dashboard', { signal }),
    enabled,
  });
}

export function useTimer(enabled = true) {
  return useQuery({
    queryKey: dk.timer,
    queryFn: async ({ signal }) => (await api.get<TimeEntry | null | undefined>('/agency/time/timer', { signal })) ?? null,
    enabled,
  });
}
