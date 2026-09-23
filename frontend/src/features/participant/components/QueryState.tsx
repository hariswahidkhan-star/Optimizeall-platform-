import type { UseQueryResult } from '@tanstack/react-query';
import type { ReactNode } from 'react';
import { Card } from '@/components/ui/Card';
import { ErrorState } from '@/components/ui/ErrorState';
import { SkeletonText } from '@/components/ui/Skeleton';

interface QueryStateProps<T> {
  query: UseQueryResult<T>;
  /** Heading shown when the request fails. */
  errorTitle?: string;
  loading?: ReactNode;
  headingLevel?: 2 | 3 | 4;
  children: (data: T) => ReactNode;
}

/** Standard loading / error / success rendering for a query. */
export function QueryState<T>({
  query,
  errorTitle,
  loading,
  headingLevel = 2,
  children,
}: QueryStateProps<T>) {
  if (query.isPending) {
    return (
      <div aria-busy="true" className="pp-loading">
        <span className="visually-hidden" role="status">
          Loading…
        </span>
        {loading ?? (
          <Card flat className="pp-pad">
            <SkeletonText lines={4} />
          </Card>
        )}
      </div>
    );
  }
  if (query.isError) {
    return (
      <Card flat>
        <ErrorState
          error={query.error}
          title={errorTitle}
          headingLevel={headingLevel}
          onRetry={() => void query.refetch()}
          retrying={query.isFetching}
        />
      </Card>
    );
  }
  return <>{children(query.data as T)}</>;
}
