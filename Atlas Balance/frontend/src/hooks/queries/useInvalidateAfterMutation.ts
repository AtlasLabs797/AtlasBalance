import { useCallback } from 'react';
import { useQueryClient } from '@tanstack/react-query';
import { mutationInvalidation, type QueryClientApi } from '@/queries/invalidation';

type MutationKind = keyof typeof mutationInvalidation;

export function useInvalidateAfterMutation(): (kind: MutationKind) => Promise<void> {
  const queryClient = useQueryClient();
  return useCallback(
    (kind: MutationKind) => {
      const invalidator = mutationInvalidation[kind] as (qc: QueryClientApi) => Promise<void>;
      return invalidator(queryClient);
    },
    [queryClient]
  );
}
