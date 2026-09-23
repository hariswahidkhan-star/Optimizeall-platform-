import { useEffect, useState, type ImgHTMLAttributes } from 'react';
import { ImageOff } from 'lucide-react';
import { api } from '../lib/api/client';
import { Skeleton } from './ui';

/**
 * Loads an image that requires authentication (e.g. `/api/v1/files/{id}` submission screenshots, which are
 * private and cannot be requested by a plain `<img src>`), and renders it from a revocable object URL.
 */
export function useProtectedImageUrl(src: string | null | undefined): { url: string | null; loading: boolean; failed: boolean } {
  const [state, setState] = useState<{ url: string | null; loading: boolean; failed: boolean }>({
    url: null,
    loading: Boolean(src),
    failed: false,
  });

  useEffect(() => {
    if (!src) {
      setState({ url: null, loading: false, failed: false });
      return;
    }
    const controller = new AbortController();
    let objectUrl: string | null = null;
    setState({ url: null, loading: true, failed: false });
    api
      .blob(src, { signal: controller.signal })
      .then((blob) => {
        objectUrl = URL.createObjectURL(blob);
        setState({ url: objectUrl, loading: false, failed: false });
      })
      .catch((error: unknown) => {
        if (error instanceof DOMException && error.name === 'AbortError') return;
        setState({ url: null, loading: false, failed: true });
      });
    return () => {
      controller.abort();
      if (objectUrl) URL.revokeObjectURL(objectUrl);
    };
  }, [src]);

  return state;
}

export interface ProtectedImageProps extends Omit<ImgHTMLAttributes<HTMLImageElement>, 'src'> {
  src: string | null | undefined;
  /** Required: describe what the image shows (e.g. "Screenshot of the submitted post"). */
  alt: string;
}

export function ProtectedImage({ src, alt, className, ...rest }: ProtectedImageProps) {
  const { url, loading, failed } = useProtectedImageUrl(src);
  if (loading)
    return (
      <span role="status" aria-label={`Loading ${alt}`}>
        <Skeleton className={className} />
      </span>
    );
  if (failed || !url)
    return (
      <div className={className} role="img" aria-label={`${alt} (unavailable)`}>
        <ImageOff aria-hidden="true" /> Image unavailable
      </div>
    );
  return <img src={url} alt={alt} className={className} {...rest} />;
}
