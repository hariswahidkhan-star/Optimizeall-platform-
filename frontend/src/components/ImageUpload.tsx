import { useState } from 'react';
import { Button, FileDrop, useToast } from '@/components/ui';
import { api } from '@/lib/api/client';
import { errorMessage } from '@/lib/api/errors';

/** `POST /admin/files` response (StoredFileDto). `url` is the app-relative `/api/v1/files/{id}`. */
export interface UploadedImage {
  id: string;
  url: string;
  contentType: string;
  sizeBytes: number;
  width: number | null;
  height: number | null;
  sha256: string;
  originalFileName: string;
  purpose: string;
  isPublic: boolean;
  createdAt: string;
}

/**
 * Uploads a public image (campaign creative or content image) to `POST /admin/files` and hands back the stored file.
 * Uploads are the default way to add images: the web app's CSP only allows its own origin plus configured image hosts.
 */
export function ImageUpload({
  label,
  onUploaded,
  disabled,
  purpose = 'CampaignAsset',
}: {
  label: string;
  onUploaded: (file: UploadedImage) => void;
  disabled?: boolean;
  purpose?: 'CampaignAsset' | 'ContentImage';
}) {
  const toast = useToast();
  const [file, setFile] = useState<File | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const upload = async () => {
    if (!file) return;
    setBusy(true);
    setError(null);
    try {
      const form = new FormData();
      form.append('file', file);
      form.append('purpose', purpose);
      const uploaded = await api.upload<UploadedImage>('/admin/files', form);
      onUploaded(uploaded);
      setFile(null);
      toast.success('Image uploaded');
    } catch (err) {
      setError(errorMessage(err));
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="stack" style={{ ['--stack-gap' as string]: 'var(--space-2)' }}>
      <FileDrop
        label={label}
        value={file}
        onChange={setFile}
        maxSizeBytes={10 * 1024 * 1024}
        hint="PNG, JPEG or WebP, up to 10 MB, at least 200×200 px. Location and other metadata are removed."
        error={error}
        disabled={disabled || busy}
      />
      {file && (
        <div>
          <Button size="sm" onClick={upload} loading={busy} disabled={disabled}>
            Upload image
          </Button>
        </div>
      )}
    </div>
  );
}
