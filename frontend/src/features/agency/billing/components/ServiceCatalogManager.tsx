import { Pencil, Plus, Trash2 } from 'lucide-react';
import { useEffect, useState } from 'react';
import {
  Badge,
  Button,
  Checkbox,
  ConfirmDialog,
  DataTable,
  EmptyState,
  ErrorState,
  FormField,
  Input,
  Money,
  Select,
  useToast,
  type DataTableColumn,
} from '@/components/ui';
import { useSupportedCurrencies } from '@/lib/api/meta';
import { useDeleteCatalogItem, useSaveCatalogItem, useServiceCatalog, useTaxRates } from '../api/hooks';
import type { CatalogItem, Recurrence } from '../api/types';
import { billingErrorMessage } from '../lib';
import { FormDialog } from './FormDialog';

const RECURRENCE: { value: Recurrence; label: string }[] = [
  { value: 'OneTime', label: 'One-time' },
  { value: 'Monthly', label: 'Monthly' },
  { value: 'Quarterly', label: 'Quarterly' },
  { value: 'Annually', label: 'Annually' },
];

function CatalogDialog({ item, open, onClose }: { item: CatalogItem | null; open: boolean; onClose: () => void }) {
  const save = useSaveCatalogItem(item?.id);
  const rates = useTaxRates();
  const toast = useToast();
  const [name, setName] = useState('');
  const [description, setDescription] = useState('');
  const [slug, setSlug] = useState('');
  const [currency, setCurrency] = useState('USD');
  const [price, setPrice] = useState('0');
  const [quantity, setQuantity] = useState('1');
  const [recurrence, setRecurrence] = useState<Recurrence>('OneTime');
  const [taxRateId, setTaxRateId] = useState('');
  const [sortOrder, setSortOrder] = useState('100');
  const [active, setActive] = useState(true);
  const currencies = useSupportedCurrencies(currency);
  useEffect(() => {
    if (!open) return;
    setName(item?.name ?? '');
    setDescription(item?.description ?? '');
    setSlug(item?.serviceSlug ?? '');
    setCurrency(item?.currency ?? 'USD');
    setPrice(String(item?.unitPrice ?? 0));
    setQuantity(String(item?.quantity ?? 1));
    setRecurrence(item?.recurrence ?? 'OneTime');
    setTaxRateId(item?.taxRateId ?? '');
    setSortOrder(String(item?.sortOrder ?? 100));
    setActive(item?.isActive ?? true);
  }, [open, item]);
  return (
    <FormDialog
      open={open}
      onClose={onClose}
      title={item ? `Edit ${item.name}` : 'New catalog item'}
      description="Adding an item to a document copies its values; editing the catalog never changes existing proposals, contracts or invoices."
      submitLabel="Save item"
      canSubmit={name.trim().length >= 2 && description.trim().length > 0}
      onSubmit={async () => {
        await save.mutateAsync({
          name: name.trim(),
          description: description.trim(),
          serviceSlug: slug.trim().toLowerCase() || null,
          currency,
          unitPrice: Number(price) || 0,
          quantity: Number(quantity) || 1,
          recurrence,
          taxRateId: taxRateId || null,
          sortOrder: Number(sortOrder) || 0,
          isActive: active,
          concurrencyStamp: item?.concurrencyStamp,
        });
        toast.success('Catalog item saved');
      }}
    >
      <FormField label="Name" required hint="Shown in the Add from catalog menu.">
        <Input value={name} maxLength={120} onChange={(e) => setName(e.target.value)} />
      </FormField>
      <FormField label="Line description" required hint="Copied onto the document line.">
        <Input value={description} maxLength={500} onChange={(e) => setDescription(e.target.value)} />
      </FormField>
      <div className="bill-grid">
        <FormField label="Currency">
          <Select value={currency} options={currencies.options} onChange={(e) => setCurrency(e.target.value)} />
        </FormField>
        <FormField label="Unit price" required>
          <Input type="number" min={0} step="any" value={price} onChange={(e) => setPrice(e.target.value)} />
        </FormField>
        <FormField label="Default quantity">
          <Input type="number" min={0} step="any" value={quantity} onChange={(e) => setQuantity(e.target.value)} />
        </FormField>
        <FormField label="Billing" hint="Used on proposals; contracts and invoices bill per period.">
          <Select value={recurrence} options={RECURRENCE} onChange={(e) => setRecurrence(e.target.value as Recurrence)} />
        </FormField>
        <FormField label="Tax rate">
          <Select
            value={taxRateId}
            options={[{ value: '', label: 'No tax' }, ...(rates.data ?? []).map((r) => ({ value: r.id, label: r.name }))]}
            onChange={(e) => setTaxRateId(e.target.value)}
          />
        </FormField>
        <FormField label="Service" optional hint="Service slug for revenue reports, e.g. seo">
          <Input value={slug} maxLength={100} onChange={(e) => setSlug(e.target.value)} />
        </FormField>
        <FormField label="Sort order">
          <Input type="number" min={0} max={10000} value={sortOrder} onChange={(e) => setSortOrder(e.target.value)} />
        </FormField>
      </div>
      <Checkbox label="Active (offered in editors)" checked={active} onChange={(e) => setActive(e.target.checked)} />
    </FormDialog>
  );
}

/** Service catalog (line-item presets) under Billing settings. */
export function ServiceCatalogManager({ canEdit }: { canEdit: boolean }) {
  const catalog = useServiceCatalog(true);
  const remove = useDeleteCatalogItem();
  const toast = useToast();
  const [editing, setEditing] = useState<CatalogItem | null>(null);
  const [open, setOpen] = useState(false);
  const [deleting, setDeleting] = useState<CatalogItem | null>(null);
  const columns: DataTableColumn<CatalogItem>[] = [
    { id: 'name', header: 'Item', primary: true, cell: (i) => i.name },
    { id: 'price', header: 'Price', align: 'right', cell: (i) => <Money amount={i.unitPrice} currency={i.currency} /> },
    { id: 'billing', header: 'Billing', cell: (i) => RECURRENCE.find((r) => r.value === i.recurrence)?.label ?? i.recurrence },
    { id: 'tax', header: 'Tax', cell: (i) => i.taxName ?? '—', hideOnMobile: true },
    { id: 'status', header: 'Status', cell: (i) => <Badge tone={i.isActive ? 'success' : 'neutral'}>{i.isActive ? 'Active' : 'Inactive'}</Badge> },
  ];
  if (catalog.isError) return <ErrorState error={catalog.error} onRetry={() => void catalog.refetch()} />;
  return (
    <div className="stack">
      {canEdit && (
        <div>
          <Button
            size="sm"
            leadingIcon={<Plus />}
            onClick={() => {
              setEditing(null);
              setOpen(true);
            }}
          >
            Add catalog item
          </Button>
        </div>
      )}
      <DataTable
        caption="Service catalog"
        columns={columns}
        rows={catalog.data ?? []}
        getRowId={(i) => i.id}
        loading={catalog.isPending}
        rowLabel={(i) => i.name}
        rowActions={
          canEdit
            ? (i) => [
                {
                  id: 'edit',
                  label: 'Edit',
                  icon: <Pencil />,
                  onSelect: () => {
                    setEditing(i);
                    setOpen(true);
                  },
                },
                { id: 'delete', label: 'Delete', icon: <Trash2 />, danger: true, onSelect: () => setDeleting(i) },
              ]
            : undefined
        }
        emptyState={<EmptyState compact headingLevel={3} title="No catalog items" description="Add what you sell to fill document lines in one click." />}
      />
      <CatalogDialog item={editing} open={open} onClose={() => setOpen(false)} />
      <ConfirmDialog
        open={deleting !== null}
        onClose={() => setDeleting(null)}
        tone="danger"
        title={`Delete “${deleting?.name ?? ''}”?`}
        description="Documents that already use it keep their lines. Deactivate it instead if you may offer it again."
        confirmLabel="Delete"
        onConfirm={async () => {
          if (!deleting) return;
          try {
            await remove.mutateAsync(deleting.id);
            toast.success('Catalog item deleted');
          } catch (error) {
            throw new Error(billingErrorMessage(error));
          }
        }}
      />
    </div>
  );
}
