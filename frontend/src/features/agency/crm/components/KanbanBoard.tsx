import clsx from 'clsx';
import { GripVertical } from 'lucide-react';
import { useState, type DragEvent, type KeyboardEvent } from 'react';
import { Link } from 'react-router-dom';
import { Money } from '@/components/ui';
import type { BoardColumn, DealSummary, Stage } from '../api/types';

export interface KanbanBoardProps {
  columns: BoardColumn[];
  canMove: boolean;
  /** Called when a deal is dropped on another stage (pointer or keyboard). */
  onMove: (deal: DealSummary, to: Stage) => void;
}

interface Picked {
  deal: DealSummary;
  from: number;
  target: number;
}

/**
 * Pipeline board. Pointer users drag cards between stages; keyboard users focus a card's move handle, press Enter or
 * Space to pick it up, Left/Right arrows to choose the stage, Enter to drop and Escape to cancel. Every step is
 * announced in a polite live region.
 */
export function KanbanBoard({ columns, canMove, onMove }: KanbanBoardProps) {
  const [picked, setPicked] = useState<Picked | null>(null);
  const [dragOver, setDragOver] = useState<string | null>(null);
  const [announcement, setAnnouncement] = useState('');

  const drop = (deal: DealSummary, targetIndex: number) => {
    const target = columns[targetIndex]?.stage;
    if (target && target.id !== deal.stageId) onMove(deal, target);
  };

  const onHandleKeyDown = (event: KeyboardEvent<HTMLButtonElement>, deal: DealSummary, columnIndex: number) => {
    const key = event.key;
    if (!picked || picked.deal.id !== deal.id) {
      if (key === 'Enter' || key === ' ') {
        event.preventDefault();
        setPicked({ deal, from: columnIndex, target: columnIndex });
        setAnnouncement(
          `Picked up ${deal.title} in ${columns[columnIndex]!.stage.name}. Use the left and right arrow keys to choose a stage, Enter to drop, Escape to cancel.`,
        );
      }
      return;
    }
    if (key === 'ArrowRight' || key === 'ArrowLeft') {
      event.preventDefault();
      const next = Math.min(columns.length - 1, Math.max(0, picked.target + (key === 'ArrowRight' ? 1 : -1)));
      setPicked({ ...picked, target: next });
      setAnnouncement(`${deal.title}: move to ${columns[next]!.stage.name}?`);
    } else if (key === 'Enter' || key === ' ') {
      event.preventDefault();
      const target = picked.target;
      setPicked(null);
      if (target === picked.from) setAnnouncement(`${deal.title} stays in ${columns[target]!.stage.name}.`);
      else {
        setAnnouncement(`Moving ${deal.title} to ${columns[target]!.stage.name}.`);
        drop(deal, target);
      }
    } else if (key === 'Escape') {
      event.preventDefault();
      setPicked(null);
      setAnnouncement(`Move cancelled. ${deal.title} stays in ${columns[picked.from]!.stage.name}.`);
    }
  };

  const onDrop = (event: DragEvent<HTMLElement>, columnIndex: number) => {
    event.preventDefault();
    setDragOver(null);
    const dealId = event.dataTransfer.getData('text/plain');
    const deal = columns.flatMap((c) => c.deals).find((d) => d.id === dealId);
    if (deal) drop(deal, columnIndex);
  };

  return (
    <div className="crm-board-wrap">
      <p className="visually-hidden" aria-live="polite" role="status">
        {announcement}
      </p>
      <div className="crm-board">
        {columns.map((column, columnIndex) => {
          const headingId = `stage-${column.stage.id}`;
          const isTarget = picked?.target === columnIndex || dragOver === column.stage.id;
          return (
            <section
              key={column.stage.id}
              aria-labelledby={headingId}
              className={clsx('crm-column', isTarget && 'crm-column--target', column.stage.kind !== 'Open' && 'crm-column--closed')}
              onDragOver={(e) => {
                if (!canMove) return;
                e.preventDefault();
                setDragOver(column.stage.id);
              }}
              onDragLeave={() => setDragOver((current) => (current === column.stage.id ? null : current))}
              onDrop={(e) => canMove && onDrop(e, columnIndex)}
            >
              <header className="crm-column__head">
                <h2 id={headingId} className="crm-column__title">
                  {column.stage.name} <span className="crm-count">({column.count})</span>
                </h2>
                <p className="crm-muted">
                  {column.stage.winProbability}% ·{' '}
                  {column.totals.length === 0
                    ? 'no value'
                    : column.totals.map((t, i) => (
                        <span key={t.currency}>
                          {i > 0 && ', '}
                          <Money amount={t.amount} currency={t.currency} compact />
                        </span>
                      ))}
                </p>
              </header>
              <ul className="crm-column__cards" aria-label={`Deals in ${column.stage.name}`}>
                {column.deals.map((deal) => (
                  <li
                    key={deal.id}
                    className={clsx('crm-card', picked?.deal.id === deal.id && 'crm-card--picked')}
                    draggable={canMove}
                    onDragStart={(e) => {
                      e.dataTransfer.setData('text/plain', deal.id);
                      e.dataTransfer.effectAllowed = 'move';
                    }}
                  >
                    <div className="crm-card__top">
                      <Link to={`/agency/crm/deals/${deal.id}`} className="ui-link crm-card__title">
                        {deal.title}
                      </Link>
                      {canMove && (
                        <button
                          type="button"
                          className="crm-card__handle"
                          aria-label={`Move ${deal.title}`}
                          aria-pressed={picked?.deal.id === deal.id}
                          onKeyDown={(e) => onHandleKeyDown(e, deal, columnIndex)}
                          onKeyUp={(e) => {
                            if (e.key === ' ') e.preventDefault();
                          }}
                        >
                          <GripVertical aria-hidden="true" />
                        </button>
                      )}
                    </div>
                    <p className="crm-muted">{[deal.companyName, deal.contactName].filter(Boolean).join(' · ') || 'No company'}</p>
                    <p className="crm-card__meta">
                      <Money amount={deal.value} currency={deal.currency} />
                      {deal.owner && <span className="crm-muted">{deal.owner.displayName}</span>}
                      {deal.score !== null && <span className="crm-muted">Score {deal.score}</span>}
                    </p>
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
