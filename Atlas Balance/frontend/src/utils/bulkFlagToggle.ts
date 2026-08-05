export type BulkFlagAction = 'flag' | 'unflag';

export interface BulkFlagToggle {
  action: BulkFlagAction;
  targetIds: string[];
}

interface BulkFlagRow {
  id: string;
  flagged: boolean;
}

export function computeBulkFlagToggle(rows: BulkFlagRow[]): BulkFlagToggle {
  if (rows.length === 0) {
    return { action: 'flag', targetIds: [] };
  }

  const allFlagged = rows.every((row) => row.flagged);
  if (allFlagged) {
    return { action: 'unflag', targetIds: rows.map((row) => row.id) };
  }

  return {
    action: 'flag',
    targetIds: rows.filter((row) => !row.flagged).map((row) => row.id),
  };
}