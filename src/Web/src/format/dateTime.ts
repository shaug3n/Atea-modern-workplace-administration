const FALLBACK = '—';

function parse(iso?: string | null): number | null {
  if (!iso) return null;
  const time = new Date(iso).getTime();
  return Number.isNaN(time) ? null : time;
}

export function formatDateTime(iso?: string | null): string {
  const time = parse(iso);
  if (time === null) return FALLBACK;
  return new Intl.DateTimeFormat(undefined, { dateStyle: 'medium', timeStyle: 'short' }).format(time);
}

export function formatDate(iso?: string | null): string {
  const time = parse(iso);
  if (time === null) return FALLBACK;
  return new Intl.DateTimeFormat(undefined, { dateStyle: 'medium' }).format(time);
}

export function formatRelative(iso?: string | null, now: number = Date.now()): string {
  const time = parse(iso);
  if (time === null) return FALLBACK;
  const elapsed = now - time;
  if (elapsed < -45_000) return formatDateTime(iso);
  const seconds = Math.max(0, elapsed) / 1000;
  if (seconds < 45) return 'just now';
  const minutes = Math.floor(seconds / 60);
  if (minutes < 60) return `${Math.max(1, minutes)} min ago`;
  const hours = Math.floor(minutes / 60);
  if (hours < 24) return `${hours} h ago`;
  if (hours < 48) return 'yesterday';
  return formatDate(iso);
}
