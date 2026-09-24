import { useMutation, useQueryClient } from '@tanstack/react-query';
import { AlertTriangle, CheckSquare, MessageSquare } from 'lucide-react';
import { useEffect, useId, useRef, useState, type DragEvent, type KeyboardEvent } from 'react';
import { Alert, Avatar, Badge, FormField, Select } from '@/components/ui';
import { api } from '@/lib/api/client';
import { errorMessage } from '@/lib/api/errors';
import { useIsMobile } from '@/lib/hooks/useMediaQuery';
import { TASK_STATUSES, type TaskStatus, type TaskSummary } from '../shared/deliveryTypes';
import { formatDateOnly, taskStatusLabel } from '../shared/deliveryUi';
import { dk } from './api';

interface Props {
  projectId: string;
  tasks: TaskSummary[];
  onOpen: (taskId: string) => void;
  canEdit: boolean;
}

interface MoveArgs {
  task: TaskSummary;
  status: TaskStatus;
  afterTaskId: string | null;
}

function columnOf(tasks: TaskSummary[], status: TaskStatus) {
  return tasks.filter((t) => t.status === status).sort((a, b) => a.sortOrder - b.sortOrder);
}

function CardContent({
  task,
  onOpen,
  onKeyDown,
  describedBy,
}: {
  task: TaskSummary;
  onOpen: (id: string) => void;
  onKeyDown: (e: KeyboardEvent<HTMLButtonElement>) => void;
  describedBy: string;
}) {
  return (
    <>
      <button
        type="button"
        className="dl-card__title"
        data-task-id={task.id}
        aria-describedby={describedBy}
        onClick={() => onOpen(task.id)}
        onKeyDown={onKeyDown}
      >
        {task.title}
      </button>
      <span className="dl-meta">
        {task.dueDate ? <span className={task.isOverdue ? 'dl-overdue' : undefined}>{task.isOverdue ? 'Overdue · ' : 'Due '}{formatDateOnly(task.dueDate)}</span> : null}
        {task.checklistTotal > 0 ? (
          <span>
            <CheckSquare aria-hidden="true" size={14} /> {task.checklistDone}/{task.checklistTotal}
          </span>
        ) : null}
        {task.commentCount > 0 ? (
          <span>
            <MessageSquare aria-hidden="true" size={14} /> {task.commentCount}
          </span>
        ) : null}
      </span>
      <span className="dl-row">
        {task.priority === 'High' || task.priority === 'Urgent' ? <Badge tone="warning" size="sm">{task.priority}</Badge> : null}
        {task.isBlocked ? (
          <Badge tone="danger" size="sm" icon={<AlertTriangle aria-hidden="true" size={12} />}>
            Blocked
          </Badge>
        ) : null}
        {task.clientVisible ? <Badge size="sm">Client visible</Badge> : null}
        {task.assignees.map((a) => (
          <Avatar key={a.id} name={a.displayName} size={24} />
        ))}
      </span>
    </>
  );
}

/**
 * Kanban board. Mouse: drag cards between columns. Keyboard: focus a card, then ←/→ moves it to the previous/next
 * column and ↑/↓ reorders it (each move is saved and announced). Phones get one column at a time with a
 * "Move to" select on each card.
 */
export function Kanban({ projectId, tasks, onOpen, canEdit }: Props) {
  const qc = useQueryClient();
  const isMobile = useIsMobile();
  const instructionsId = useId();
  const [announcement, setAnnouncement] = useState('');
  const [dragging, setDragging] = useState<string | null>(null);
  const [dropTarget, setDropTarget] = useState<TaskStatus | null>(null);
  const [mobileColumn, setMobileColumn] = useState<TaskStatus>('Todo');

  // The card to focus once it has re-rendered in its new place (a move to another column mounts a new element).
  const [focus, setFocus] = useState<{ id: string; status: TaskStatus; sortOrder: number } | null>(null);
  // Arrow keys pressed while a move is still saving: applied in order once it is saved, on the saved task (its new
  // column and concurrency stamp), so two quick presses move a card two columns instead of failing with a conflict.
  const queued = useRef<{ taskId: string; key: string }[]>([]);
  const saving = useRef(false);

  useEffect(() => {
    if (!focus) return;
    // Wait until the board shows the saved position (the card may still sit in its old column for a render).
    const shown = tasks.find((t) => t.id === focus.id);
    if (!shown || shown.status !== focus.status || shown.sortOrder !== focus.sortOrder) return;
    document.querySelector<HTMLElement>(`[data-task-id="${focus.id}"]`)?.focus();
    setFocus(null);
  }, [focus, tasks]);

  const move = useMutation({
    mutationFn: ({ task, status, afterTaskId }: MoveArgs) =>
      api.post<TaskSummary>(`/agency/tasks/${task.id}/move`, { status, afterTaskId, concurrencyStamp: task.concurrencyStamp }),
    onSuccess: (updated, { task }) => {
      const current = (qc.getQueryData<TaskSummary[]>(dk.tasks(projectId)) ?? tasks).map((t) => (t.id === updated.id ? updated : t));
      qc.setQueryData<TaskSummary[]>(dk.tasks(projectId), current);
      const column = columnOf(current, updated.status);
      const position = column.findIndex((t) => t.id === updated.id) + 1;
      setAnnouncement(`Moved ${task.title} to ${taskStatusLabel(updated.status)}, position ${position} of ${column.length}.`);
      // Keep keyboard focus on the moved card (it re-renders in its new column).
      setFocus({ id: updated.id, status: updated.status, sortOrder: updated.sortOrder });
      saving.current = false;
      const next = queued.current.shift();
      if (next) {
        const queuedTask = current.find((t) => t.id === next.taskId);
        if (queuedTask && applyKey(next.key, queuedTask, current)) return;
      }
      queued.current = [];
      void qc.invalidateQueries({ queryKey: dk.tasks(projectId) });
    },
    onError: (error) => {
      saving.current = false;
      queued.current = [];
      setAnnouncement(`Couldn't move the task: ${errorMessage(error)}`);
      void qc.invalidateQueries({ queryKey: dk.tasks(projectId) });
    },
  });

  const moveTo = (task: TaskSummary, status: TaskStatus, index?: number, all: TaskSummary[] = tasks) => {
    const column = columnOf(all, status).filter((t) => t.id !== task.id);
    const at = index === undefined ? column.length : Math.max(0, Math.min(index, column.length));
    saving.current = true;
    move.mutate({ task, status, afterTaskId: at === 0 ? null : column[at - 1]!.id });
  };

  /** Applies an arrow key to `task` within `all`; false when the key does nothing (edge of the board). */
  const applyKey = (key: string, task: TaskSummary, all: TaskSummary[]) => {
    const col = TASK_STATUSES.indexOf(task.status);
    const column = columnOf(all, task.status);
    const index = column.findIndex((t) => t.id === task.id);
    if (key === 'ArrowRight' && col < TASK_STATUSES.length - 1) moveTo(task, TASK_STATUSES[col + 1]!, undefined, all);
    else if (key === 'ArrowLeft' && col > 0) moveTo(task, TASK_STATUSES[col - 1]!, undefined, all);
    else if (key === 'ArrowUp' && index > 0) moveTo(task, task.status, index - 1, all);
    else if (key === 'ArrowDown' && index < column.length - 1) moveTo(task, task.status, index + 1, all);
    else return false;
    return true;
  };

  const onKeyDown = (e: KeyboardEvent<HTMLElement>, task: TaskSummary) => {
    if (!canEdit || !['ArrowRight', 'ArrowLeft', 'ArrowUp', 'ArrowDown'].includes(e.key)) return;
    if (saving.current) {
      e.preventDefault();
      queued.current.push({ taskId: task.id, key: e.key });
      return;
    }
    if (applyKey(e.key, task, tasks)) e.preventDefault();
  };

  const onDrop = (e: DragEvent, status: TaskStatus) => {
    e.preventDefault();
    const task = tasks.find((t) => t.id === dragging);
    setDragging(null);
    setDropTarget(null);
    if (task && task.status !== status) moveTo(task, status);
  };

  const statuses = isMobile ? [mobileColumn] : TASK_STATUSES;
  return (
    <div className="dl-page">
      <p id={instructionsId} className="visually-hidden">
        {canEdit ? 'Task card: arrow left and right move the task to another column, arrow up and down reorder it, Enter opens it.' : 'Enter opens the task.'}
      </p>
      <div role="status" aria-live="polite" className="visually-hidden">
        {announcement}
      </div>
      {move.error ? <Alert tone="danger">{errorMessage(move.error)}</Alert> : null}
      {isMobile ? (
        <FormField label="Column">
          <Select
            value={mobileColumn}
            onChange={(e) => setMobileColumn(e.target.value as TaskStatus)}
            options={TASK_STATUSES.map((s) => ({ value: s, label: `${taskStatusLabel(s)} (${columnOf(tasks, s).length})` }))}
          />
        </FormField>
      ) : null}
      <div className="dl-kanban" data-layout={isMobile ? 'single' : 'board'}>
        {statuses.map((status) => {
          const column = columnOf(tasks, status);
          const headingId = `${instructionsId}-${status}`;
          return (
            <section
              key={status}
              className="dl-kanban__column"
              aria-labelledby={headingId}
              data-drop={dropTarget === status}
              onDragOver={(e) => {
                if (!canEdit) return;
                e.preventDefault();
                setDropTarget(status);
              }}
              onDragLeave={() => setDropTarget(null)}
              onDrop={(e) => onDrop(e, status)}
            >
              <h2 id={headingId} className="dl-kanban__head">
                <span>{taskStatusLabel(status)}</span>
                <Badge size="sm">{column.length}</Badge>
              </h2>
              <ul className="dl-kanban__list" aria-label={`${taskStatusLabel(status)} tasks`}>
                {column.map((task) => (
                  <li
                    key={task.id}
                    className="dl-card"
                    data-dragging={dragging === task.id}
                    draggable={canEdit}
                    onDragStart={() => setDragging(task.id)}
                    onDragEnd={() => setDragging(null)}
                  >
                    <CardContent task={task} onOpen={onOpen} onKeyDown={(e) => onKeyDown(e, task)} describedBy={instructionsId} />
                    {isMobile && canEdit ? (
                      <Select
                        size="sm"
                        aria-label={`Move ${task.title} to`}
                        value={task.status}
                        onChange={(e) => moveTo(task, e.target.value as TaskStatus)}
                        options={TASK_STATUSES.map((s) => ({ value: s, label: taskStatusLabel(s) }))}
                      />
                    ) : null}
                  </li>
                ))}
              </ul>
            </section>
          );
        })}
      </div>
    </div>
  );
}
