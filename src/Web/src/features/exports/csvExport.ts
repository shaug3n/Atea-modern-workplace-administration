import type { ApiFetch } from '../users/usersApi';

export type CsvExportInfo = { rowCount: number; maximum: number; truncated: boolean };

export async function downloadCsv(api: ApiFetch, path: string, fileName: string): Promise<CsvExportInfo> {
  const response = await api(path, { cache: 'no-store' });
  if (!response.ok) {
    const body = await response.json().catch(() => ({})) as { error?: string };
    throw new Error(body.error || 'Export failed');
  }
  const rowCount = Number(response.headers.get('X-Export-Row-Count'));
  const maximum = Number(response.headers.get('X-Export-Max-Rows'));
  const truncated = response.headers.get('X-Export-Truncated');
  if (!Number.isSafeInteger(rowCount) || !Number.isSafeInteger(maximum) || maximum <= 0 || rowCount < 0 || (truncated !== 'true' && truncated !== 'false')) {
    throw new Error('Export metadata is unavailable');
  }
  const blob = await response.blob();
  const url = URL.createObjectURL(blob);
  try {
    const link = document.createElement('a');
    link.href = url;
    link.download = fileName;
    document.body.append(link);
    link.click();
    link.remove();
  } finally {
    URL.revokeObjectURL(url);
  }
  return { rowCount, maximum, truncated: truncated === 'true' };
}

export function exportStatus(info: CsvExportInfo): string {
  const count = `${info.rowCount.toLocaleString()} ${info.rowCount === 1 ? 'row' : 'rows'}`;
  return info.truncated
    ? `${count} exported. The ${info.maximum.toLocaleString()} row maximum was reached; this CSV is truncated.`
    : `${count} exported. Complete filtered result.`;
}
