import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { CheckCircle2, Copy, Download, FileText, Pencil, Plus, RotateCcw, Send, Trash2 } from 'lucide-react';
import { useState, type FormEvent } from 'react';
import {
  Alert,
  Badge,
  Button,
  ConfirmDialog,
  DataTable,
  DateTime,
  Dialog,
  EmptyState,
  FormField,
  Input,
  Select,
  Textarea,
  useToast,
  type DataTableColumn,
} from '@/components/ui';
import { api } from '@/lib/api/client';
import { errorMessage } from '@/lib/api/errors';
import { seoKeys, type Brief, type BriefStatus, type Site } from './api';
import { fieldErrors, firstError, lines } from './common';

const statusLabel: Record<BriefStatus, string> = { Draft: 'Draft', Ready: 'Ready for writing', HandedOff: 'Handed off', Published: 'Published (done)' };
const statusTone: Record<BriefStatus, 'neutral' | 'info' | 'brand' | 'success'> = { Draft: 'neutral', Ready: 'info', HandedOff: 'brand', Published: 'success' };

/** Full PUT body of a brief with a new status (the API replaces every field). */
const withStatus = (b: Brief, status: BriefStatus) => ({
  title: b.title, targetKeyword: b.targetKeyword, relatedKeywords: b.relatedKeywords, questions: b.questions, outline: b.outline,
  wordCountTarget: b.wordCountTarget, competitorUrls: b.competitorUrls, notes: b.notes, status, concurrencyStamp: b.concurrencyStamp,
});

export function BriefsPanel({ site }: { site: Site }) {
  const queryClient = useQueryClient();
  const toast = useToast();
  const [editing, setEditing] = useState<Brief | 'new' | null>(null);
  const [handoff, setHandoff] = useState<{ message: string; markdown: string } | null>(null);
  const briefs = useQuery({ queryKey: seoKeys.briefs(site.id), queryFn: () => api.get<Brief[]>(`/agency/seo/sites/${site.id}/briefs`) });
  const refresh = () => void queryClient.invalidateQueries({ queryKey: seoKeys.briefs(site.id) });
  const [deleting, setDeleting] = useState<Brief | null>(null);
  const setStatus = useMutation({
    mutationFn: ({ b, status }: { b: Brief; status: BriefStatus }) => api.put<Brief>(`/agency/seo/briefs/${b.id}`, withStatus(b, status)),
    onSuccess: (b) => {
      toast.success(b.status === 'Published' ? 'Brief marked done' : 'Brief reopened');
      refresh();
    },
    onError: (err) => toast.error('Could not update the brief', errorMessage(err)),
  });
  const duplicate = useMutation({
    mutationFn: (b: Brief) => api.post<Brief>(`/agency/seo/briefs/${b.id}/duplicate`),
    onSuccess: (b) => {
      toast.success('Brief duplicated', b.title);
      refresh();
    },
    onError: (err) => toast.error('Could not duplicate the brief', errorMessage(err)),
  });
  const handOff = useMutation({
    mutationFn: (b: Brief) => api.post<{ brief: Brief; taskCreated: boolean; message: string; markdown: string }>(`/agency/seo/briefs/${b.id}/handoff`),
    onSuccess: (r) => {
      setHandoff(r);
      refresh();
    },
    onError: (err) => toast.error('Hand-off failed', errorMessage(err)),
  });

  const columns: DataTableColumn<Brief>[] = [
    {
      id: 'title',
      header: 'Brief',
      primary: true,
      cell: (b) => (
        <span className="stack seo-stack-xs">
          <span className="seo-strong">{b.title}</span>
          <span className="text-small text-muted">Target: {b.targetKeyword}</span>
        </span>
      ),
    },
    { id: 'words', header: 'Words', align: 'right', cell: (b) => b.wordCountTarget.toLocaleString('en') },
    { id: 'status', header: 'Status', cell: (b) => <Badge tone={statusTone[b.status]}>{statusLabel[b.status]}</Badge> },
    { id: 'updated', header: 'Updated', hideOnMobile: true, cell: (b) => <DateTime value={b.updatedAt} format="relative" /> },
  ];

  return (
    <div className="stack">
      <div className="seo-toolbar">
        <Button leadingIcon={<Plus />} onClick={() => setEditing('new')}>
          New content brief
        </Button>
      </div>
      <DataTable
        caption="Content briefs"
        columns={columns}
        rows={briefs.data ?? []}
        getRowId={(b) => b.id}
        rowLabel={(b) => b.title}
        loading={briefs.isLoading}
        rowActions={(b) => [
          { id: 'edit', label: 'Edit', icon: <Pencil />, onSelect: () => setEditing(b) },
          {
            id: 'export',
            label: 'Download Markdown',
            icon: <Download />,
            onSelect: () => void api.download(`/agency/seo/briefs/${b.id}/export.md`, 'brief.md'),
          },
          { id: 'handoff', label: 'Hand off to content team', icon: <Send />, onSelect: () => handOff.mutate(b), disabled: b.status === 'Published' },
          b.status === 'Published'
            ? { id: 'reopen', label: 'Reopen as draft', icon: <RotateCcw />, onSelect: () => setStatus.mutate({ b, status: 'Draft' }) }
            : { id: 'done', label: 'Mark published (done)', icon: <CheckCircle2 />, onSelect: () => setStatus.mutate({ b, status: 'Published' }) },
          { id: 'duplicate', label: 'Duplicate', icon: <Copy />, onSelect: () => duplicate.mutate(b) },
          { id: 'delete', label: 'Delete', icon: <Trash2 />, danger: true, onSelect: () => setDeleting(b) },
        ]}
        emptyState={<EmptyState compact headingLevel={3} icon={<FileText />} title="No briefs yet" description="Plan articles around a target keyword with an outline and competitor pages." />}
      />
      {editing && <BriefDialog siteId={site.id} brief={editing === 'new' ? null : editing} onClose={() => setEditing(null)} onSaved={refresh} />}
      <ConfirmDialog
        open={deleting !== null}
        onClose={() => setDeleting(null)}
        tone="danger"
        title="Delete this brief?"
        description={deleting ? `“${deleting.title}” is removed. Download the Markdown first if you still need it.` : undefined}
        confirmLabel="Delete brief"
        onConfirm={async () => {
          if (!deleting) return;
          await api.delete(`/agency/seo/briefs/${deleting.id}`);
          toast.success('Brief deleted');
          refresh();
        }}
      />
      {handoff && (
        <Dialog open onClose={() => setHandoff(null)} title="Brief handed off" size="lg">
          <div className="stack">
            <Alert tone="info" title="Next step">
              {handoff.message}
            </Alert>
            <FormField label="Brief (Markdown)">
              <Textarea readOnly rows={14} value={handoff.markdown} />
            </FormField>
          </div>
        </Dialog>
      )}
    </div>
  );
}

function BriefDialog({ siteId, brief, onClose, onSaved }: { siteId: string; brief: Brief | null; onClose: () => void; onSaved: () => void }) {
  const [form, setForm] = useState({
    title: brief?.title ?? '',
    targetKeyword: brief?.targetKeyword ?? '',
    relatedKeywords: brief?.relatedKeywords.join('\n') ?? '',
    questions: brief?.questions.join('\n') ?? '',
    outline: brief?.outline.join('\n') ?? '## Introduction\n## \n## Conclusion',
    wordCountTarget: String(brief?.wordCountTarget ?? 1200),
    competitorUrls: brief?.competitorUrls.join('\n') ?? '',
    notes: brief?.notes ?? '',
    status: brief?.status ?? ('Draft' as BriefStatus),
  });
  const save = useMutation({
    mutationFn: () => {
      const body = {
        title: form.title,
        targetKeyword: form.targetKeyword,
        relatedKeywords: lines(form.relatedKeywords),
        questions: lines(form.questions),
        outline: form.outline.split(/\r?\n/).filter((l) => l.trim() && l.trim() !== '##'),
        wordCountTarget: Number(form.wordCountTarget) || 1200,
        competitorUrls: lines(form.competitorUrls),
        notes: form.notes || null,
        status: form.status,
        concurrencyStamp: brief?.concurrencyStamp,
      };
      return brief ? api.put(`/agency/seo/briefs/${brief.id}`, body) : api.post(`/agency/seo/sites/${siteId}/briefs`, body);
    },
    onSuccess: () => {
      onSaved();
      onClose();
    },
  });
  const errors = fieldErrors(save.error);
  return (
    <Dialog
      open
      onClose={onClose}
      size="lg"
      title={brief ? 'Edit brief' : 'New content brief'}
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Cancel
          </Button>
          <Button type="submit" form="seo-brief" loading={save.isPending}>
            Save brief
          </Button>
        </>
      }
    >
      <form id="seo-brief" className="stack" onSubmit={(e: FormEvent) => (e.preventDefault(), save.mutate())}>
        {save.isError && <Alert tone="danger" title="Could not save the brief">{errorMessage(save.error)}</Alert>}
        <div className="seo-grid-2">
          <FormField label="Working title" required error={firstError(errors, 'title')}>
            <Input value={form.title} onChange={(e) => setForm({ ...form, title: e.target.value })} />
          </FormField>
          <FormField label="Target keyword" required error={firstError(errors, 'targetKeyword')}>
            <Input value={form.targetKeyword} onChange={(e) => setForm({ ...form, targetKeyword: e.target.value })} />
          </FormField>
        </div>
        <div className="seo-grid-2">
          <FormField label="Related keywords" optional hint="One per line.">
            <Textarea rows={4} value={form.relatedKeywords} onChange={(e) => setForm({ ...form, relatedKeywords: e.target.value })} />
          </FormField>
          <FormField label="Questions to answer" optional hint="One per line (People Also Ask, sales questions).">
            <Textarea rows={4} value={form.questions} onChange={(e) => setForm({ ...form, questions: e.target.value })} />
          </FormField>
        </div>
        <FormField label="Outline" hint="One heading per line; start with ## or ### for H2/H3.">
          <Textarea rows={6} value={form.outline} onChange={(e) => setForm({ ...form, outline: e.target.value })} />
        </FormField>
        <div className="seo-grid-2">
          <FormField label="Word count target" error={firstError(errors, 'wordCountTarget')}>
            <Input type="number" min={100} max={20000} value={form.wordCountTarget} onChange={(e) => setForm({ ...form, wordCountTarget: e.target.value })} />
          </FormField>
          <FormField label="Status">
            <Select value={form.status} onChange={(e) => setForm({ ...form, status: e.target.value as BriefStatus })} options={(['Draft', 'Ready', 'HandedOff', 'Published'] as const).map((s) => ({ value: s, label: statusLabel[s] }))} />
          </FormField>
        </div>
        <FormField label="Competitor URLs" optional hint="Pages currently ranking that we want to beat, one per line." error={firstError(errors, 'competitorUrls')}>
          <Textarea rows={3} value={form.competitorUrls} onChange={(e) => setForm({ ...form, competitorUrls: e.target.value })} />
        </FormField>
        <FormField label="Notes for the writer" optional>
          <Textarea rows={3} value={form.notes} onChange={(e) => setForm({ ...form, notes: e.target.value })} />
        </FormField>
      </form>
    </Dialog>
  );
}
