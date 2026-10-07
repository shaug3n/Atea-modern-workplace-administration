import React from 'react';
import { formatDateTime, formatRelative } from '../format/dateTime';

export function DateTime({ value, relative = false }: { value?: string | null; relative?: boolean }) {
  if (!value || Number.isNaN(new Date(value).getTime())) return <>—</>;
  const absolute = formatDateTime(value);
  const rel = formatRelative(value);
  return <time dateTime={value} title={relative ? absolute : rel}>{relative ? rel : absolute}</time>;
}
