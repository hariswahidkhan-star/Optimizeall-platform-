import '@/features/learning/learning.css';
import { CheckCircle2, Plus, Trash2 } from 'lucide-react';
import { useEffect, useState, type ReactNode } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { Alert } from '@/components/ui/Alert';
import { Button } from '@/components/ui/Button';
import { Card, CardBody, CardHeader } from '@/components/ui/Card';
import { FormField } from '@/components/ui/FormField';
import { IconButton } from '@/components/ui/IconButton';
import { Input } from '@/components/ui/Input';
import { PageHeader } from '@/components/ui/PageHeader';
import { Select } from '@/components/ui/Select';
import { Skeleton } from '@/components/ui/Skeleton';
import { Switch } from '@/components/ui/Switch';
import { Tabs } from '@/components/ui/Tabs';
import { Textarea } from '@/components/ui/Textarea';
import { useToast } from '@/components/ui/toastContext';
import { QueryError } from '../shared/common';
import { adminErrorMessage } from '../shared/errors';
import {
  createCourse,
  useAdminCourse,
  useCourseMutations,
  useCourseVersion,
  validateDocument,
  type CoursePack,
  type PackCheck,
  type PackIssue,
  type PackLesson,
  type PackModule,
  type PackQuestion,
  type ValidationReport,
} from './api';

const CATEGORY_VALUES = ['sales', 'marketing', 'seo', 'ai', 'business', 'design', 'data', 'platform'];

function blankCheck(): PackCheck {
  return { question: '', options: ['', '', ''], correct: [0], explanation: '' };
}

function blankLesson(n: number): PackLesson {
  return {
    slug: `lesson-${n}`,
    title: '',
    type: 'article',
    durationMinutes: 10,
    body: '',
    video: null,
    keyTakeaways: ['', '', ''],
    knowledgeCheck: [blankCheck(), blankCheck()],
    activity: null,
  };
}

function blankModule(n: number): PackModule {
  return { slug: `module-${n}`, title: '', summary: '', lessons: [blankLesson(1)] };
}

export function blankCourse(): CoursePack {
  return {
    slug: '',
    version: 1,
    title: '',
    subtitle: '',
    category: 'marketing',
    level: 'beginner',
    estimatedMinutes: 60,
    description: '',
    outcomes: ['', '', '', '', ''],
    skills: ['', '', ''],
    prerequisites: [],
    badge: { name: '', description: '', criteria: '' },
    passingScore: 80,
    modules: [blankModule(1)],
    finalExam: { questionCount: 10, timeLimitMinutes: 20, maxAttemptsPerDay: 3, pool: [] },
  };
}

/** One item per line (outcomes, skills, takeaways). */
function Lines({ label, value, onChange, hint }: { label: string; value: string[]; onChange: (v: string[]) => void; hint?: ReactNode }) {
  return (
    <FormField label={label} hint={hint ?? 'One per line.'}>
      <Textarea rows={Math.max(3, value.length + 1)} value={value.join('\n')} onChange={(e) => onChange(e.target.value.split('\n'))} />
    </FormField>
  );
}

/** Multiple-choice editor shared by knowledge checks and the exam pool (validated by the same contract on the server). */
function McqEditor<T extends PackCheck>({
  value,
  onChange,
  onRemove,
  label,
  multipleAllowed,
  children,
}: {
  value: T;
  onChange: (v: T) => void;
  onRemove: () => void;
  label: string;
  multipleAllowed: boolean;
  children?: ReactNode;
}) {
  const setOption = (i: number, text: string) => onChange({ ...value, options: value.options.map((o, j) => (j === i ? text : o)) });
  const toggleCorrect = (i: number) => {
    const has = value.correct.includes(i);
    const correct = multipleAllowed ? (has ? value.correct.filter((c) => c !== i) : [...value.correct, i].sort()) : [i];
    onChange({ ...value, correct });
  };
  const removeOption = (i: number) =>
    onChange({
      ...value,
      options: value.options.filter((_, j) => j !== i),
      correct: value.correct.filter((c) => c !== i).map((c) => (c > i ? c - 1 : c)),
    });
  return (
    <fieldset className="lx-mcq">
      <legend className="lx-mcq__legend">
        {label}
        <IconButton label={`Remove ${label}`} icon={<Trash2 />} variant="ghost" size="sm" onClick={onRemove} />
      </legend>
      {children}
      <FormField label="Question">
        <Textarea rows={2} value={value.question} onChange={(e) => onChange({ ...value, question: e.target.value })} />
      </FormField>
      <div className="lx-mcq__options" role="group" aria-label="Options (tick the correct ones)">
        {value.options.map((o, i) => (
          <div key={i} className="lx-mcq__option">
            <input
              type={multipleAllowed ? 'checkbox' : 'radio'}
              aria-label={`Option ${i + 1} is correct`}
              checked={value.correct.includes(i)}
              onChange={() => toggleCorrect(i)}
            />
            <Input aria-label={`Option ${i + 1}`} value={o} onChange={(e) => setOption(i, e.target.value)} />
            <IconButton label={`Remove option ${i + 1}`} icon={<Trash2 />} variant="ghost" size="sm" onClick={() => removeOption(i)} />
          </div>
        ))}
        {value.options.length < 5 && (
          <Button size="sm" variant="ghost" leadingIcon={<Plus />} onClick={() => onChange({ ...value, options: [...value.options, ''] })}>
            Add option
          </Button>
        )}
      </div>
      <FormField label="Explanation" hint="Why the right answer is right and the others are not.">
        <Textarea rows={2} value={value.explanation} onChange={(e) => onChange({ ...value, explanation: e.target.value })} />
      </FormField>
    </fieldset>
  );
}

function LessonEditor({ lesson, onChange, onRemove }: { lesson: PackLesson; onChange: (l: PackLesson) => void; onRemove: () => void }) {
  return (
    <details className="lx-editor-block" open={!lesson.title}>
      <summary>{lesson.title || lesson.slug || 'New lesson'}</summary>
      <div className="stack">
        <div className="lx-editor-grid">
          <FormField label="Slug">
            <Input value={lesson.slug} onChange={(e) => onChange({ ...lesson, slug: e.target.value })} />
          </FormField>
          <FormField label="Title">
            <Input value={lesson.title} onChange={(e) => onChange({ ...lesson, title: e.target.value })} />
          </FormField>
          <FormField label="Type">
            <Select
              value={lesson.type}
              onChange={(e) => {
                const type = e.target.value as 'article' | 'video';
                onChange({ ...lesson, type, video: type === 'video' ? lesson.video ?? { script: '', src: null, poster: null, captions: null } : null });
              }}
              options={[
                { value: 'article', label: 'Article' },
                { value: 'video', label: 'Video (article + video)' },
              ]}
            />
          </FormField>
          <FormField label="Minutes">
            <Input type="number" min={1} value={lesson.durationMinutes} onChange={(e) => onChange({ ...lesson, durationMinutes: Number(e.target.value) })} />
          </FormField>
        </div>
        <FormField label="Body (Markdown, headings from ###)">
          <Textarea rows={14} value={lesson.body} onChange={(e) => onChange({ ...lesson, body: e.target.value })} />
        </FormField>
        {lesson.video && (
          <FormField label="Video narration script" hint="Voiced with ElevenLabs / HeyGen. Set the video files on the course’s Videos tab.">
            <Textarea rows={6} value={lesson.video.script} onChange={(e) => onChange({ ...lesson, video: { ...lesson.video!, script: e.target.value } })} />
          </FormField>
        )}
        <Lines label="Key takeaways" value={lesson.keyTakeaways} onChange={(keyTakeaways) => onChange({ ...lesson, keyTakeaways })} />
        <FormField label="Activity (optional)">
          <Textarea rows={2} value={lesson.activity ?? ''} onChange={(e) => onChange({ ...lesson, activity: e.target.value || null })} />
        </FormField>
        <h4 className="lx-subhead">Knowledge check (2–4 questions)</h4>
        {lesson.knowledgeCheck.map((c, i) => (
          <McqEditor
            key={i}
            label={`Knowledge check ${i + 1}`}
            value={c}
            multipleAllowed
            onChange={(v) => onChange({ ...lesson, knowledgeCheck: lesson.knowledgeCheck.map((x, j) => (j === i ? v : x)) })}
            onRemove={() => onChange({ ...lesson, knowledgeCheck: lesson.knowledgeCheck.filter((_, j) => j !== i) })}
          />
        ))}
        <div className="lx-actions">
          <Button size="sm" variant="secondary" leadingIcon={<Plus />} onClick={() => onChange({ ...lesson, knowledgeCheck: [...lesson.knowledgeCheck, blankCheck()] })}>
            Add knowledge check
          </Button>
          <Button size="sm" variant="ghost" leadingIcon={<Trash2 />} onClick={onRemove}>
            Remove lesson
          </Button>
        </div>
      </div>
    </details>
  );
}

function IssueList({ report }: { report: ValidationReport | null }) {
  if (!report) return null;
  if (report.valid)
    return (
      <Alert tone="success" title="The course matches the course pack contract">
        {report.moduleCount} modules · {report.lessonCount} lessons · {report.poolSize} pool questions for {report.questionCount} per attempt.
      </Alert>
    );
  return (
    <Alert tone="danger" title={`${report.issues.length} problem${report.issues.length === 1 ? '' : 's'} to fix`}>
      <ul className="lx-issues">
        {report.issues.map((i: PackIssue, n) => (
          <li key={n}>
            <code>{i.path}</code>: {i.message}
          </li>
        ))}
      </ul>
    </Alert>
  );
}

function Editor({ initial, mode, onSave, saving }: { initial: CoursePack; mode: 'create' | 'edit'; onSave: (doc: CoursePack, publish: boolean, note: string) => void; saving: boolean }) {
  const [doc, setDoc] = useState<CoursePack>(initial);
  const [report, setReport] = useState<ValidationReport | null>(null);
  const [publish, setPublish] = useState(false);
  const [note, setNote] = useState('');
  const [json, setJson] = useState('');
  const [tab, setTab] = useState('course');
  const toast = useToast();
  const set = <K extends keyof CoursePack>(key: K, value: CoursePack[K]) => setDoc({ ...doc, [key]: value });
  const setModule = (i: number, m: PackModule) => set('modules', doc.modules.map((x, j) => (j === i ? m : x)));
  const clean = (d: CoursePack): CoursePack => ({
    ...d,
    outcomes: d.outcomes.map((s) => s.trim()).filter(Boolean),
    skills: d.skills.map((s) => s.trim()).filter(Boolean),
    prerequisites: d.prerequisites.map((s) => s.trim()).filter(Boolean),
    modules: d.modules.map((m) => ({ ...m, lessons: m.lessons.map((l) => ({ ...l, keyTakeaways: l.keyTakeaways.map((s) => s.trim()).filter(Boolean) })) })),
  });

  const validate = async () => {
    try {
      const r = await validateDocument(clean(doc));
      setReport(r);
      return r.valid;
    } catch (e) {
      toast.error('Validation failed', adminErrorMessage(e));
      return false;
    }
  };

  const pool = doc.finalExam.pool;
  const setPool = (p: PackQuestion[]) => set('finalExam', { ...doc.finalExam, pool: p });

  return (
    <div className="stack">
      <Tabs
        label="Course editor sections"
        value={tab}
        onValueChange={setTab}
        tabs={[
          {
            id: 'course',
            label: 'Course',
            content: (
              <Card>
                <CardBody className="stack">
                  <div className="lx-editor-grid">
                    <FormField label="Slug" hint={mode === 'edit' ? 'The slug cannot change.' : 'kebab-case, e.g. email-marketing-basics'}>
                      <Input value={doc.slug} disabled={mode === 'edit'} onChange={(e) => set('slug', e.target.value)} />
                    </FormField>
                    <FormField label="Title">
                      <Input value={doc.title} maxLength={80} onChange={(e) => set('title', e.target.value)} />
                    </FormField>
                    <FormField label="Category">
                      <Select value={doc.category} onChange={(e) => set('category', e.target.value)} options={CATEGORY_VALUES.map((c) => ({ value: c, label: c }))} />
                    </FormField>
                    <FormField label="Level">
                      <Select
                        value={doc.level}
                        onChange={(e) => set('level', e.target.value)}
                        options={['beginner', 'intermediate', 'advanced'].map((l) => ({ value: l, label: l }))}
                      />
                    </FormField>
                    <FormField label="Estimated minutes">
                      <Input type="number" min={1} value={doc.estimatedMinutes} onChange={(e) => set('estimatedMinutes', Number(e.target.value))} />
                    </FormField>
                    <FormField label="Passing score (%)">
                      <Input type="number" min={50} max={100} value={doc.passingScore} onChange={(e) => set('passingScore', Number(e.target.value))} />
                    </FormField>
                  </div>
                  <FormField label="Subtitle">
                    <Input value={doc.subtitle} maxLength={140} onChange={(e) => set('subtitle', e.target.value)} />
                  </FormField>
                  <FormField label="Description (Markdown)">
                    <Textarea rows={5} value={doc.description} onChange={(e) => set('description', e.target.value)} />
                  </FormField>
                  <Lines label="Outcomes (5–8)" value={doc.outcomes} onChange={(v) => set('outcomes', v)} />
                  <Lines label="Skills (3–8)" value={doc.skills} onChange={(v) => set('skills', v)} />
                  <Lines label="Prerequisites (course slugs, optional)" value={doc.prerequisites} onChange={(v) => set('prerequisites', v)} />
                  <h3 className="lx-subhead">Badge and certificate</h3>
                  <FormField label="Badge name (max 60)">
                    <Input value={doc.badge.name} maxLength={60} onChange={(e) => set('badge', { ...doc.badge, name: e.target.value })} />
                  </FormField>
                  <FormField label="Badge description">
                    <Textarea rows={3} value={doc.badge.description} onChange={(e) => set('badge', { ...doc.badge, description: e.target.value })} />
                  </FormField>
                  <FormField label="Criteria">
                    <Input value={doc.badge.criteria} onChange={(e) => set('badge', { ...doc.badge, criteria: e.target.value })} />
                  </FormField>
                </CardBody>
              </Card>
            ),
          },
          {
            id: 'modules',
            label: `Modules & lessons (${doc.modules.length})`,
            content: (
              <div className="stack">
                {doc.modules.map((m, i) => (
                  <Card key={i} as="section" aria-label={`Module ${i + 1}`}>
                    <CardHeader
                      title={`Module ${i + 1}: ${m.title || m.slug}`}
                      headingLevel={3}
                      actions={
                        <IconButton label={`Remove module ${i + 1}`} icon={<Trash2 />} variant="ghost" onClick={() => set('modules', doc.modules.filter((_, j) => j !== i))} />
                      }
                    />
                    <CardBody className="stack">
                      <div className="lx-editor-grid">
                        <FormField label="Module slug">
                          <Input value={m.slug} onChange={(e) => setModule(i, { ...m, slug: e.target.value })} />
                        </FormField>
                        <FormField label="Module title">
                          <Input value={m.title} onChange={(e) => setModule(i, { ...m, title: e.target.value })} />
                        </FormField>
                      </div>
                      <FormField label="Summary">
                        <Input value={m.summary} onChange={(e) => setModule(i, { ...m, summary: e.target.value })} />
                      </FormField>
                      {m.lessons.map((l, k) => (
                        <LessonEditor
                          key={k}
                          lesson={l}
                          onChange={(nl) => setModule(i, { ...m, lessons: m.lessons.map((x, j) => (j === k ? nl : x)) })}
                          onRemove={() => setModule(i, { ...m, lessons: m.lessons.filter((_, j) => j !== k) })}
                        />
                      ))}
                      <div>
                        <Button size="sm" variant="secondary" leadingIcon={<Plus />} onClick={() => setModule(i, { ...m, lessons: [...m.lessons, blankLesson(m.lessons.length + 1)] })}>
                          Add lesson
                        </Button>
                      </div>
                    </CardBody>
                  </Card>
                ))}
                <div>
                  <Button variant="secondary" leadingIcon={<Plus />} onClick={() => set('modules', [...doc.modules, blankModule(doc.modules.length + 1)])}>
                    Add module
                  </Button>
                </div>
              </div>
            ),
          },
          {
            id: 'exam',
            label: `Final exam (${pool.length})`,
            content: (
              <div className="stack">
                <Card>
                  <CardBody className="lx-editor-grid">
                    <FormField label="Questions per attempt">
                      <Input type="number" min={1} value={doc.finalExam.questionCount} onChange={(e) => set('finalExam', { ...doc.finalExam, questionCount: Number(e.target.value) })} />
                    </FormField>
                    <FormField label="Time limit (minutes)">
                      <Input type="number" min={1} value={doc.finalExam.timeLimitMinutes} onChange={(e) => set('finalExam', { ...doc.finalExam, timeLimitMinutes: Number(e.target.value) })} />
                    </FormField>
                    <FormField label="Attempts per 24 hours">
                      <Input type="number" min={1} value={doc.finalExam.maxAttemptsPerDay} onChange={(e) => set('finalExam', { ...doc.finalExam, maxAttemptsPerDay: Number(e.target.value) })} />
                    </FormField>
                  </CardBody>
                </Card>
                <p className="text-muted text-small">
                  The pool needs at least 1.5 × questions per attempt ({Math.ceil(doc.finalExam.questionCount * 1.5)}), must cover every module, 3–5 options
                  per question, and no “all/none of the above”.
                </p>
                {pool.map((q, i) => (
                  <Card key={i}>
                    <CardBody>
                      <McqEditor
                        label={`Pool question ${i + 1}`}
                        value={q}
                        multipleAllowed={q.type === 'multiple'}
                        onChange={(v) => setPool(pool.map((x, j) => (j === i ? v : x)))}
                        onRemove={() => setPool(pool.filter((_, j) => j !== i))}
                      >
                        <div className="lx-editor-grid">
                          <FormField label="Id">
                            <Input value={q.id} onChange={(e) => setPool(pool.map((x, j) => (j === i ? { ...q, id: e.target.value } : x)))} />
                          </FormField>
                          <FormField label="Module">
                            <Select
                              value={q.module}
                              onChange={(e) => setPool(pool.map((x, j) => (j === i ? { ...q, module: e.target.value } : x)))}
                              options={doc.modules.map((m) => ({ value: m.slug, label: m.title || m.slug }))}
                            />
                          </FormField>
                          <FormField label="Difficulty">
                            <Select
                              value={q.difficulty}
                              onChange={(e) => setPool(pool.map((x, j) => (j === i ? { ...q, difficulty: e.target.value as PackQuestion['difficulty'] } : x)))}
                              options={['easy', 'medium', 'hard'].map((d) => ({ value: d, label: d }))}
                            />
                          </FormField>
                          <FormField label="Type">
                            <Select
                              value={q.type}
                              onChange={(e) =>
                                setPool(pool.map((x, j) => (j === i ? { ...q, type: e.target.value as PackQuestion['type'], correct: q.correct.slice(0, e.target.value === 'single' ? 1 : 5) } : x)))
                              }
                              options={[
                                { value: 'single', label: 'Single answer' },
                                { value: 'multiple', label: 'Choose all that apply' },
                              ]}
                            />
                          </FormField>
                        </div>
                      </McqEditor>
                    </CardBody>
                  </Card>
                ))}
                <div>
                  <Button
                    variant="secondary"
                    leadingIcon={<Plus />}
                    onClick={() =>
                      setPool([
                        ...pool,
                        { ...blankCheck(), options: ['', '', '', ''], id: `${doc.slug || 'q'}-${String(pool.length + 1).padStart(3, '0')}`, module: doc.modules[0]?.slug ?? '', difficulty: 'medium', type: 'single' },
                      ])
                    }
                  >
                    Add pool question
                  </Button>
                </div>
              </div>
            ),
          },
          {
            id: 'json',
            label: 'Import JSON',
            content: (
              <Card>
                <CardBody className="stack">
                  <FormField label="Course pack JSON" hint="Paste a course pack (docs/LEARNING.md) to load it into the editor.">
                    <Textarea rows={12} value={json} onChange={(e) => setJson(e.target.value)} />
                  </FormField>
                  <div>
                    <Button
                      variant="secondary"
                      onClick={() => {
                        try {
                          const parsed = JSON.parse(json) as CoursePack;
                          setDoc(mode === 'edit' ? { ...parsed, slug: doc.slug } : parsed);
                          setTab('course');
                          toast.success('Loaded into the editor', 'Validate, then save.');
                        } catch {
                          toast.error('That is not valid JSON');
                        }
                      }}
                    >
                      Load JSON
                    </Button>
                  </div>
                </CardBody>
              </Card>
            ),
          },
        ]}
      />
      <Card as="section" aria-label="Validate and save">
        <CardBody className="stack">
          <IssueList report={report} />
          <FormField label="Version note (optional)">
            <Input value={note} maxLength={500} onChange={(e) => setNote(e.target.value)} />
          </FormField>
          <Switch checked={publish} onCheckedChange={setPublish} label="Publish this version right away" />
          <div className="lx-actions">
            <Button variant="secondary" leadingIcon={<CheckCircle2 />} onClick={() => void validate()}>
              Validate
            </Button>
            <Button
              loading={saving}
              onClick={async () => {
                if (await validate()) onSave(clean(doc), publish, note);
              }}
            >
              {mode === 'create' ? 'Create course' : 'Save as new version'}
            </Button>
          </div>
        </CardBody>
      </Card>
    </div>
  );
}

export function NewCoursePage() {
  const navigate = useNavigate();
  const toast = useToast();
  const [saving, setSaving] = useState(false);
  return (
    <>
      <PageHeader title="New course" breadcrumbs={[{ label: 'Learning', to: '/admin/learning' }, { label: 'New course' }]} />
      <Editor
        initial={blankCourse()}
        mode="create"
        saving={saving}
        onSave={async (doc, publish) => {
          setSaving(true);
          try {
            const created = await createCourse(doc, publish);
            toast.success('Course created');
            navigate(`/admin/learning/courses/${created.summary.id}`);
          } catch (e) {
            toast.error('The course wasn’t created', adminErrorMessage(e));
          } finally {
            setSaving(false);
          }
        }}
      />
    </>
  );
}

export function EditCoursePage() {
  const { courseId = '' } = useParams();
  const course = useAdminCourse(courseId);
  const latest = course.data?.versions.find((v) => v.isLatest);
  const doc = useCourseVersion(courseId, latest?.id);
  const m = useCourseMutations(courseId);
  const navigate = useNavigate();
  const toast = useToast();
  const [ready, setReady] = useState(false);
  useEffect(() => setReady(!!doc.data), [doc.data]);

  if (course.isError) return <QueryError error={course.error} />;
  if (doc.isError) return <QueryError error={doc.error} />;
  if (!course.data || !doc.data || !ready) return <Skeleton height={400} />;
  return (
    <>
      <PageHeader
        title={`Edit: ${course.data.summary.title}`}
        description={`Editing version ${doc.data.number}. Saving creates a new version; ${course.data.summary.origin === 'Pack' ? 'the course pack file is never changed.' : 'earlier versions are kept.'}`}
        breadcrumbs={[
          { label: 'Learning', to: '/admin/learning' },
          { label: course.data.summary.title, to: `/admin/learning/courses/${courseId}` },
          { label: 'Edit' },
        ]}
      />
      <Editor
        key={doc.data.id}
        initial={doc.data.document}
        mode="edit"
        saving={m.saveVersion.isPending}
        onSave={(document, publish, note) =>
          m.saveVersion.mutate(
            { document, basedOnVersionId: doc.data.id, note: note || undefined, publish, concurrencyStamp: course.data.summary.concurrencyStamp },
            {
              onSuccess: () => {
                toast.success('New version saved', publish ? 'It is now live.' : 'Publish it from the course page when ready.');
                navigate(`/admin/learning/courses/${courseId}`);
              },
              onError: (e) => toast.error('The version wasn’t saved', adminErrorMessage(e)),
            },
          )
        }
      />
    </>
  );
}
