import React from 'react';
import { Icon } from './icons';

export type StatusPillFilterOption = {
  value: string;
  label: string;
  count: number;
  selected: boolean;
};

export type StatusPillFilterProps = {
  label: string;
  options: readonly StatusPillFilterOption[];
  onToggle: (value: string) => void;
};

export function StatusPillFilter({ label, options, onToggle }: StatusPillFilterProps) {
  return (
    <div className="status-pill-filter" role="group" aria-label={label}>
      {options.map((option) => (
        <button
          className="status-pill-filter__option"
          key={option.value}
          type="button"
          aria-pressed={option.selected}
          onClick={() => onToggle(option.value)}
        >
          <span>{option.label}</span>
          <span className="status-pill-filter__count">{option.count}</span>
          {option.selected && <Icon name="check" size={14} />}
        </button>
      ))}
    </div>
  );
}
