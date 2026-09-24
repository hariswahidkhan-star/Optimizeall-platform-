import clsx from 'clsx';
import { ArrowDown, ArrowUp, GripVertical, RotateCcw, Save } from 'lucide-react';
import { useEffect, useId, useRef, useState, type KeyboardEvent, type ReactNode } from 'react';
import { Button } from '@/components/ui/Button';
import { IconButton } from '@/components/ui/IconButton';

export interface ReorderListProps<T> {
  items: T[];
  getId: (item: T) => string;
  /** Plain-text name used in announcements and button labels. */
  getLabel: (item: T) => string;
  renderItem: (item: T) => ReactNode;
  /** Accessible name of the list. */
  label: string;
  /** Persist the new order (ids in display order). */
  onSave: (ids: string[]) => Promise<unknown>;
  saving?: boolean;
  disabled?: boolean;
}

function move<T>(list: T[], from: number, to: number): T[] {
  if (to < 0 || to >= list.length || from === to) return list;
  const copy = [...list];
  const [item] = copy.splice(from, 1);
  copy.splice(to, 0, item as T);
  return copy;
}

/**
 * Keyboard-accessible reordering. Each row has a handle: press Space or Enter to pick the item up, the arrow keys
 * (or Home/End) to move it, Space/Enter to drop and Escape to cancel. Pointer users drag rows by the handle (or the
 * row), and "Move up/down" buttons serve switch users. Changes are announced in a live region and saved explicitly
 * with "Save order".
 */
export function ReorderList<T>({
  items,
  getId,
  getLabel,
  renderItem,
  label,
  onSave,
  saving,
  disabled,
}: ReorderListProps<T>) {
  const instructionsId = useId();
  const [order, setOrder] = useState<string[]>(() => items.map(getId));
  const [grabbed, setGrabbed] = useState<string | null>(null);
  const [grabOrigin, setGrabOrigin] = useState<string[] | null>(null);
  const [announcement, setAnnouncement] = useState('');
  const [focusId, setFocusId] = useState<string | null>(null);
  const [dragging, setDragging] = useState<string | null>(null);
  const handles = useRef(new Map<string, HTMLButtonElement>());

  const serverOrder = items.map(getId).join('|');
  // Reset the local order whenever the server list changes (after save or reload).
  useEffect(() => {
    setOrder(serverOrder ? serverOrder.split('|') : []);
    setGrabbed(null);
  }, [serverOrder]);

  useEffect(() => {
    if (focusId) handles.current.get(focusId)?.focus();
  }, [focusId, order]);

  const byId = new Map(items.map((item) => [getId(item), item]));
  const rows = order.map((id) => byId.get(id)).filter((item): item is T => item !== undefined);
  const dirty = order.join('|') !== serverOrder;
  const nameOf = (id: string) => {
    const item = byId.get(id);
    return item ? getLabel(item) : id;
  };

  const moveTo = (id: string, to: number) => {
    const from = order.indexOf(id);
    const target = Math.max(0, Math.min(order.length - 1, to));
    if (from === target) return;
    setOrder(move(order, from, target));
    if (!dragging) setFocusId(id);
    setAnnouncement(`${nameOf(id)} moved to position ${target + 1} of ${order.length}.`);
  };

  const onHandleKeyDown = (event: KeyboardEvent<HTMLButtonElement>, id: string) => {
    const index = order.indexOf(id);
    if (event.key === ' ' || event.key === 'Enter') {
      event.preventDefault();
      if (grabbed === id) {
        setGrabbed(null);
        setGrabOrigin(null);
        setAnnouncement(`${nameOf(id)} dropped at position ${index + 1} of ${order.length}.`);
      } else {
        setGrabbed(id);
        setGrabOrigin(order);
        setAnnouncement(
          `${nameOf(id)} picked up at position ${index + 1} of ${order.length}. Use the arrow keys to move, Space to drop, Escape to cancel.`,
        );
      }
      return;
    }
    if (event.key === 'Escape' && grabbed === id) {
      event.preventDefault();
      if (grabOrigin) setOrder(grabOrigin);
      setGrabbed(null);
      setGrabOrigin(null);
      setFocusId(id);
      setAnnouncement(`Move cancelled. ${nameOf(id)} returned to its position.`);
      return;
    }
    // Arrow keys move a grabbed item; Alt+Arrow moves without grabbing first.
    if (grabbed !== id && !event.altKey) return;
    let to: number | null = null;
    if (event.key === 'ArrowUp') to = index - 1;
    else if (event.key === 'ArrowDown') to = index + 1;
    else if (event.key === 'Home') to = 0;
    else if (event.key === 'End') to = order.length - 1;
    if (to === null) return;
    event.preventDefault();
    moveTo(id, to);
  };

  return (
    <div className="admin-reorder">
      <p id={instructionsId} className="text-small text-muted">
        Drag a row to a new position, or with the keyboard focus a handle, press Space to pick the item up, move it
        with the arrow keys and press Space again to drop it. Then choose “Save order”.
      </p>
      <ol className="admin-reorder__list" aria-label={label}>
        {rows.map((item, index) => {
          const id = getId(item);
          const name = getLabel(item);
          return (
            <li
              key={id}
              className={clsx(
                'admin-reorder__item',
                (grabbed === id || dragging === id) && 'admin-reorder__item--grabbed',
              )}
              draggable={!disabled && !saving}
              onDragStart={(event) => {
                setDragging(id);
                event.dataTransfer.effectAllowed = 'move';
                event.dataTransfer.setData('text/plain', id);
              }}
              onDragOver={(event) => {
                if (!dragging) return;
                event.preventDefault();
                if (dragging !== id) moveTo(dragging, index);
              }}
              onDrop={(event) => {
                event.preventDefault();
                if (dragging) setAnnouncement(`${nameOf(dragging)} dropped at position ${order.indexOf(dragging) + 1} of ${order.length}.`);
                setDragging(null);
              }}
              onDragEnd={() => setDragging(null)}
            >
              <button
                ref={(node) => {
                  if (node) handles.current.set(id, node);
                  else handles.current.delete(id);
                }}
                type="button"
                className="admin-reorder__handle"
                aria-label={`Reorder ${name}, position ${index + 1} of ${rows.length}`}
                aria-describedby={instructionsId}
                aria-pressed={grabbed === id}
                disabled={disabled || saving}
                onKeyDown={(event) => onHandleKeyDown(event, id)}
              >
                <GripVertical aria-hidden="true" />
              </button>
              <span className="admin-reorder__position tabular" aria-hidden="true">
                {index + 1}
              </span>
              <div className="admin-reorder__content">{renderItem(item)}</div>
              <div className="admin-reorder__buttons">
                <IconButton
                  size="sm"
                  label={`Move ${name} up`}
                  icon={<ArrowUp />}
                  disabled={disabled || saving || index === 0}
                  onClick={() => moveTo(id, index - 1)}
                />
                <IconButton
                  size="sm"
                  label={`Move ${name} down`}
                  icon={<ArrowDown />}
                  disabled={disabled || saving || index === rows.length - 1}
                  onClick={() => moveTo(id, index + 1)}
                />
              </div>
            </li>
          );
        })}
      </ol>
      <div className="visually-hidden" aria-live="assertive" aria-atomic="true">
        {announcement}
      </div>
      <div className="cluster">
        <Button
          leadingIcon={<Save />}
          disabled={!dirty || disabled}
          loading={saving}
          onClick={() => {
            void onSave(order);
          }}
        >
          Save order
        </Button>
        <Button
          variant="ghost"
          leadingIcon={<RotateCcw />}
          disabled={!dirty || saving}
          onClick={() => {
            setOrder(serverOrder.split('|'));
            setAnnouncement('Order reset.');
          }}
        >
          Reset
        </Button>
        {dirty && <span className="text-small text-muted">Unsaved order changes</span>}
      </div>
    </div>
  );
}
