/**
 * Delivery API contract (Clients + Projects modules). Mirrors backend DTOs in `Modules/Clients/ClientDtos.cs` and
 * `Modules/Projects/ProjectDtos.cs` (camelCase JSON, string enums, UTC ISO timestamps, `yyyy-MM-dd` dates).
 */

export interface Person {
  id: string;
  displayName: string;
  email: string;
}

export type ClientStatus = 'Onboarding' | 'Active' | 'Paused' | 'Churned';
export type ClientDuty = 'Viewer' | 'Approver' | 'Billing' | 'Owner';
export type ServiceRole = 'AccountManager' | 'Strategist' | 'Seo' | 'Ads' | 'Social' | 'Content' | 'Design';
export type HealthLevel = 'Green' | 'Amber' | 'Red';

export const CLIENT_STATUSES: ClientStatus[] = ['Onboarding', 'Active', 'Paused', 'Churned'];
export const CLIENT_DUTIES: ClientDuty[] = ['Viewer', 'Approver', 'Billing', 'Owner'];
export const SERVICE_ROLES: ServiceRole[] = ['AccountManager', 'Strategist', 'Seo', 'Ads', 'Social', 'Content', 'Design'];

export interface ClientSummary {
  id: string;
  name: string;
  slug: string;
  industry: string | null;
  status: ClientStatus;
  currency: string;
  countryCode: string;
  accountManager: Person | null;
  logoUrl: string | null;
  activeProjects: number;
  memberCount: number;
  createdAt: string;
}

export interface ClientDetail {
  id: string;
  name: string;
  slug: string;
  summary: string | null;
  industry: string | null;
  website: string | null;
  countryCode: string;
  timeZone: string;
  currency: string;
  status: ClientStatus;
  statusReason: string | null;
  statusChangedAt: string | null;
  accountManager: Person | null;
  logoFileId: string | null;
  logoUrl: string | null;
  billingContactName: string | null;
  billingEmail: string | null;
  billingAddress: string | null;
  taxId: string | null;
  notes: string | null;
  approvalSlaDays: number;
  autoApproveAfterDays: number | null;
  lastInvoicePaidAt: string | null;
  createdAt: string;
  updatedAt: string;
  concurrencyStamp: string;
}

export interface HealthReason {
  code: string;
  level: HealthLevel;
  message: string;
  penalty: number;
}

export interface ClientHealth {
  clientId: string;
  clientName: string;
  status: ClientStatus;
  score: number;
  level: HealthLevel;
  reasons: HealthReason[];
  overdueTasks: number;
  pendingApprovals: number;
  accountManager: Person | null;
}

export interface TeamAssignment {
  id: string;
  user: Person;
  serviceRole: ServiceRole;
  isPrimary: boolean;
  assignedAt: string;
}

export interface AccountTeamMember {
  userId: string;
  displayName: string;
  email: string;
  roles: ServiceRole[];
  isAccountManager: boolean;
  isPrimary: boolean;
}

export interface ClientMember {
  userId: string;
  displayName: string;
  email: string;
  role: ClientDuty;
  addedAt: string;
  lastLoginAt: string | null;
  hasSignedIn: boolean;
}

export interface MyOrganization {
  clientId: string;
  name: string;
  slug: string;
  status: ClientStatus;
  role: ClientDuty;
  logoUrl: string | null;
  currency: string;
  timeZone: string;
}

export type OnboardingStatus = 'Pending' | 'Done' | 'NotApplicable';

export interface OnboardingItem {
  id: string;
  key: string;
  title: string;
  description: string | null;
  category: string;
  owner: 'Agency' | 'Client';
  sortOrder: number;
  status: OnboardingStatus;
  completedAt: string | null;
  completedBy: string | null;
  note: string | null;
}

export interface Onboarding {
  items: OnboardingItem[];
  done: number;
  total: number;
  percentComplete: number;
}

export interface BrandColor {
  name: string;
  hex: string;
}

export interface BrandPersona {
  name: string;
  description: string;
}

export interface BrandAsset {
  id: string;
  kind: 'Logo' | 'Image' | 'Guideline' | 'Other';
  label: string;
  fileId: string;
  fileName: string;
  contentType: string;
  sizeBytes: number;
  staffUrl: string;
  clientUrl: string;
  createdAt: string;
}

export interface BrandKit {
  clientAccountId: string;
  colors: BrandColor[];
  fonts: string[];
  toneOfVoice: string | null;
  personas: BrandPersona[];
  competitors: string[];
  dos: string[];
  donts: string[];
  keyMessages: string[];
  assets: BrandAsset[];
  updatedAt: string;
  concurrencyStamp: string;
}

export interface FeedbackItem {
  id: string;
  kind: 'Csat' | 'Nps';
  score: number;
  comment: string | null;
  period: string | null;
  deliverableId: string | null;
  deliverableTitle: string | null;
  user: Person;
  createdAt: string;
}

export interface FeedbackSummary {
  averageCsat: number | null;
  csatResponses: number;
  npsScore: number | null;
  npsResponses: number;
  promoters: number;
  passives: number;
  detractors: number;
  recent: FeedbackItem[];
}

export interface NpsStatus {
  period: string;
  due: boolean;
  myScore: number | null;
}

export interface StaffPerson {
  id: string;
  displayName: string;
  email: string;
  roles: string[];
}

// ---------------------------------------------------------------- projects & tasks

export type ProjectType =
  | 'RetainerMonth'
  | 'OneOffCampaign'
  | 'WebsiteBuild'
  | 'SeoProgram'
  | 'SocialContent'
  | 'PaidAdsLaunch'
  | 'EmailProgram'
  | 'Other';
export type ProjectStatus = 'Planning' | 'Active' | 'OnHold' | 'Completed' | 'Cancelled';
export type TaskStatus = 'Todo' | 'InProgress' | 'InReview' | 'Blocked' | 'Done';
export type TaskPriority = 'Low' | 'Normal' | 'High' | 'Urgent';

export const PROJECT_TYPES: ProjectType[] = [
  'RetainerMonth',
  'OneOffCampaign',
  'WebsiteBuild',
  'SeoProgram',
  'SocialContent',
  'PaidAdsLaunch',
  'EmailProgram',
  'Other',
];
export const PROJECT_STATUSES: ProjectStatus[] = ['Planning', 'Active', 'OnHold', 'Completed', 'Cancelled'];
export const TASK_STATUSES: TaskStatus[] = ['Todo', 'InProgress', 'InReview', 'Blocked', 'Done'];
export const TASK_PRIORITIES: TaskPriority[] = ['Low', 'Normal', 'High', 'Urgent'];

export interface BudgetBurn {
  hoursLogged: number;
  billableHours: number;
  budgetHours: number | null;
  hoursBurnPercent: number | null;
  amountBurned: number;
  budgetAmount: number | null;
  amountBurnPercent: number | null;
  currency: string;
  unpricedHours: number;
}

export interface ProjectSummary {
  id: string;
  clientId: string;
  clientName: string;
  name: string;
  type: ProjectType;
  status: ProjectStatus;
  serviceLines: string[];
  startDate: string | null;
  endDate: string | null;
  owner: Person | null;
  openTasks: number;
  overdueTasks: number;
  totalTasks: number;
  doneTasks: number;
  budgetHours: number | null;
  hoursLogged: number;
  atRisk: boolean;
}

export interface Milestone {
  id: string;
  title: string;
  dueDate: string | null;
  status: 'Open' | 'Done';
  sortOrder: number;
  clientVisible: boolean;
  taskCount: number;
  doneCount: number;
}

export interface ProjectDetail {
  id: string;
  clientId: string;
  clientName: string;
  clientCurrency: string;
  name: string;
  description: string | null;
  type: ProjectType;
  status: ProjectStatus;
  serviceLines: string[];
  startDate: string | null;
  endDate: string | null;
  budgetHours: number | null;
  budgetAmount: number | null;
  currency: string;
  defaultHourlyRate: number | null;
  owner: Person | null;
  members: Person[];
  milestones: Milestone[];
  templateKey: string | null;
  budget: BudgetBurn;
  createdAt: string;
  concurrencyStamp: string;
}

export interface TaskSummary {
  id: string;
  projectId: string;
  projectName: string;
  clientId: string;
  clientName: string;
  milestoneId: string | null;
  title: string;
  status: TaskStatus;
  priority: TaskPriority;
  dueDate: string | null;
  estimateHours: number | null;
  labels: string[];
  clientVisible: boolean;
  sortOrder: number;
  assignees: Person[];
  checklistDone: number;
  checklistTotal: number;
  commentCount: number;
  isBlocked: boolean;
  isOverdue: boolean;
  concurrencyStamp: string;
}

export interface TaskComment {
  id: string;
  author: Person;
  body: string;
  mentions: Person[];
  createdAt: string;
}

export interface DeliveryFile {
  id: string;
  fileName: string;
  contentType: string;
  sizeBytes: number;
  createdAt: string;
  staffUrl: string;
  clientUrl: string;
}

export interface TaskDetail {
  task: TaskSummary;
  description: string | null;
  checklist: { id: string; text: string; isDone: boolean; sortOrder: number }[];
  comments: TaskComment[];
  watchers: Person[];
  blockedBy: { id: string; title: string; status: TaskStatus }[];
  blocking: { id: string; title: string; status: TaskStatus }[];
  attachments: { id: string; file: DeliveryFile; addedBy: Person; createdAt: string }[];
  hoursLogged: number;
  createdAt: string;
  completedAt: string | null;
  iWatch: boolean;
}

export interface TemplateTask {
  title: string;
  description: string | null;
  milestoneKey: string | null;
  offsetDays: number;
  estimateHours: number | null;
  labels: string[];
  clientVisible: boolean;
}

export interface ProjectTemplate {
  id: string;
  key: string;
  name: string;
  description: string | null;
  projectType: ProjectType;
  serviceLines: string[];
  defaultBudgetHours: number | null;
  durationDays: number | null;
  milestones: { key: string; title: string; offsetDays: number }[];
  tasks: TemplateTask[];
  recurring: { title: string; description: string | null; dayOfMonth: number; dueInDays: number; estimateHours: number | null; labels: string[] }[];
  isActive: boolean;
  concurrencyStamp: string;
}

// ---------------------------------------------------------------- deliverables

export type DeliverableType =
  | 'Copy'
  | 'Design'
  | 'Video'
  | 'BlogPost'
  | 'AdCreative'
  | 'Report'
  | 'LandingPage'
  | 'SocialPostSet'
  | 'Other';
export type DeliverableStatus = 'Draft' | 'InternalReview' | 'ClientReview' | 'ChangesRequested' | 'Approved' | 'Published';

export const DELIVERABLE_TYPES: DeliverableType[] = [
  'Copy',
  'Design',
  'Video',
  'BlogPost',
  'AdCreative',
  'Report',
  'LandingPage',
  'SocialPostSet',
  'Other',
];
export const DELIVERABLE_STATUSES: DeliverableStatus[] = [
  'Draft',
  'InternalReview',
  'ClientReview',
  'ChangesRequested',
  'Approved',
  'Published',
];

export interface DeliverableSummary {
  id: string;
  clientId: string;
  clientName: string;
  projectId: string;
  projectName: string;
  taskId: string | null;
  title: string;
  type: DeliverableType;
  status: DeliverableStatus;
  currentVersion: number;
  owner: Person | null;
  reviewer: Person | null;
  sentToClientAt: string | null;
  clientDueAt: string | null;
  isOverdue: boolean;
  approvedAt: string | null;
  updatedAt: string;
  concurrencyStamp: string;
}

export interface DeliverableVersion {
  id: string;
  number: number;
  file: DeliveryFile | null;
  linkUrl: string | null;
  body: string | null;
  notes: string | null;
  createdBy: Person;
  createdAt: string;
}

export interface DeliverableComment {
  id: string;
  versionNumber: number;
  author: Person;
  fromClient: boolean;
  isInternal: boolean;
  body: string;
  createdAt: string;
}

export interface DeliverableHistory {
  id: string;
  versionNumber: number;
  stage: 'Internal' | 'Client' | 'System';
  decision: string;
  userName: string | null;
  comment: string | null;
  createdAt: string;
}

export type DeliverableAction =
  | 'comment'
  | 'addVersion'
  | 'submit'
  | 'internalApprove'
  | 'internalRequestChanges'
  | 'publish'
  | 'approve'
  | 'requestChanges'
  | 'rate';

export interface DeliverableDetail {
  deliverable: DeliverableSummary;
  description: string | null;
  versions: DeliverableVersion[];
  comments: DeliverableComment[];
  history: DeliverableHistory[];
  approvedVersion: number | null;
  approvedByName: string | null;
  autoApproved: boolean;
  allowedActions: DeliverableAction[];
}

// ---------------------------------------------------------------- time

export interface TimeEntry {
  id: string;
  userId: string;
  userName: string;
  clientId: string;
  clientName: string;
  projectId: string;
  projectName: string;
  taskId: string | null;
  taskTitle: string | null;
  date: string;
  minutes: number;
  billable: boolean;
  note: string | null;
  startedAt: string | null;
  isRunning: boolean;
  locked: boolean;
  concurrencyStamp: string;
}

export type TimesheetStatus = 'Open' | 'Submitted' | 'Approved' | 'Rejected';

export interface Timesheet {
  id: string | null;
  userId: string;
  userName: string;
  weekStart: string;
  status: TimesheetStatus;
  totalMinutes: number;
  billableMinutes: number;
  days: { date: string; minutes: number }[];
  entries: TimeEntry[];
  submittedAt: string | null;
  decidedAt: string | null;
  decidedBy: string | null;
  decisionComment: string | null;
  concurrencyStamp: string | null;
}

export interface UtilizationRow {
  userId: string;
  userName: string;
  totalMinutes: number;
  billableMinutes: number;
  capacityMinutes: number;
  utilizationPercent: number;
  billablePercent: number;
}

export interface Utilization {
  from: string;
  to: string;
  rows: UtilizationRow[];
  totalMinutes: number;
  billableMinutes: number;
}

// ---------------------------------------------------------------- reports

export type KpiMeasurement = 'Measured' | 'Estimated' | 'Manual';

export interface ReportKpi {
  key: string;
  label: string;
  value: number | null;
  unit: string | null;
  previousValue: number | null;
  source: string;
  measurement: KpiMeasurement;
  note?: string | null;
}

export interface ReportSection {
  key: string;
  kind: 'summary' | 'kpis' | 'channel' | 'wins' | 'plan' | 'custom';
  title: string;
  body: string | null;
  kpis: ReportKpi[];
  providerKey?: string | null;
  providerNote?: string | null;
}

export type ReportStatus = 'Draft' | 'Published';

export interface ReportSummary {
  id: string;
  clientId: string;
  clientName: string;
  title: string;
  periodStart: string;
  periodEnd: string;
  status: ReportStatus;
  publishedAt: string | null;
  autoGenerated: boolean;
  updatedAt: string;
}

export interface Report {
  id: string;
  clientId: string;
  clientName: string;
  projectId: string | null;
  title: string;
  periodStart: string;
  periodEnd: string;
  status: ReportStatus;
  templateKey: string | null;
  sections: ReportSection[];
  publishedAt: string | null;
  publishedBy: string | null;
  availableProviders: { key: string; title: string; serviceLine: string }[];
  updatedAt: string;
  concurrencyStamp: string;
}

// ---------------------------------------------------------------- briefs, messages, meetings

export type BriefFieldType = 'Text' | 'LongText' | 'Date' | 'Url' | 'List' | 'Select';

export interface BriefTemplate {
  id: string;
  key: string;
  serviceLine: string;
  name: string;
  description: string | null;
  fields: { key: string; label: string; type: BriefFieldType; required: boolean; help: string | null; options: string[] }[];
}

export type BriefStatus = 'Submitted' | 'InReview' | 'Accepted' | 'Converted' | 'Declined';

export interface Brief {
  id: string;
  clientId: string;
  clientName: string;
  projectId: string | null;
  projectName: string | null;
  templateKey: string;
  templateName: string;
  title: string;
  status: BriefStatus;
  answers: { key: string; label: string; value: string }[];
  deadline: string | null;
  submittedBy: Person;
  submittedByClient: boolean;
  createdAt: string;
  convertedAt: string | null;
  staffNote: string | null;
  concurrencyStamp: string;
}

export interface ThreadSummary {
  id: string;
  clientId: string;
  subject: string;
  projectId: string | null;
  lastMessageAt: string;
  messageCount: number;
  unreadCount: number;
  lastMessagePreview: string | null;
  lastAuthor: string | null;
}

export interface Message {
  id: string;
  author: Person;
  fromClient: boolean;
  body: string;
  attachments: DeliveryFile[];
  createdAt: string;
  readBy: string[];
}

export interface Thread {
  id: string;
  clientId: string;
  subject: string;
  projectId: string | null;
  messages: Message[];
  participants: Person[];
}

export type MeetingKind = 'Kickoff' | 'MonthlyReview' | 'Strategy' | 'Creative' | 'Other';

export interface Meeting {
  id: string;
  clientId: string;
  clientName: string;
  projectId: string | null;
  title: string;
  kind: MeetingKind;
  startsAt: string;
  durationMinutes: number;
  location: string | null;
  agenda: string | null;
  notes: string | null;
  status: 'Scheduled' | 'Held' | 'Cancelled';
  attendees: Person[];
  actionItems: { id: string; text: string; assigneeUserId: string | null; dueDate: string | null; taskId: string | null }[];
  concurrencyStamp: string;
}

// ---------------------------------------------------------------- dashboards

export interface AgencyDashboard {
  myTasks: TaskSummary[];
  myTaskCounts: { open: number; overdue: number; dueToday: number };
  reviewQueue: DeliverableSummary[];
  pendingClientApprovals: DeliverableSummary[];
  todaysMeetings: Meeting[];
  timer: TimeEntry | null;
  myMinutesThisWeek: number;
  accountManager: {
    healthBoard: ClientHealth[];
    overdueByClient: { clientId: string; clientName: string; overdueTasks: number; overdueApprovals: number }[];
    utilization: Utilization;
  } | null;
  admin: {
    activeClients: number;
    onboardingClients: number;
    activeProjects: number;
    projectsAtRisk: ProjectSummary[];
    minutesThisWeek: number;
    billableMinutesThisWeek: number;
    deliverablesAwaitingClients: number;
  } | null;
}

export interface ClientProjectSummary {
  id: string;
  name: string;
  type: ProjectType;
  status: ProjectStatus;
  startDate: string | null;
  endDate: string | null;
  visibleTasks: number;
  doneTasks: number;
  progressPercent: number;
  nextMilestone: Milestone | null;
}

export interface ClientProjectDetail {
  project: ClientProjectSummary;
  description: string | null;
  milestones: Milestone[];
  tasks: { id: string; title: string; status: TaskStatus; dueDate: string | null; milestoneId: string | null; completedAt: string | null }[];
}

export interface ClientHome {
  organization: MyOrganization;
  onboarding: Onboarding;
  awaitingApproval: DeliverableSummary[];
  recentDeliverables: DeliverableSummary[];
  latestReport: ReportSummary | null;
  upcomingMeetings: Meeting[];
  threads: ThreadSummary[];
  team: AccountTeamMember[];
  projects: ClientProjectSummary[];
  nps: NpsStatus;
  canApprove: boolean;
}
