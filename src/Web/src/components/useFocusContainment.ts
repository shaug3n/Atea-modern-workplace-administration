import { useEffect, useRef, type RefObject } from 'react';

const focusableSelector = 'button:not([disabled]), [href], input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])';

function focusableElements(root: HTMLElement) {
  return Array.from(root.querySelectorAll<HTMLElement>(focusableSelector));
}

export function useFocusContainment<T extends HTMLElement>(
  active: boolean,
  onEscape?: () => void,
  restoreFocusRef?: RefObject<HTMLElement | null>,
) {
  const rootRef = useRef<T>(null);
  const escapeRef = useRef(onEscape);
  escapeRef.current = onEscape;
  const restoreRef = useRef<HTMLElement | null>(null);

  useEffect(() => {
    if (!active) return;
    restoreRef.current = restoreFocusRef?.current ?? (document.activeElement instanceof HTMLElement ? document.activeElement : null);
    const root = rootRef.current;
    if (!root) return;
    const elements = focusableElements(root);
    const initial = root.querySelector<HTMLElement>('button:not([disabled])') ?? elements[0];
    initial?.focus();

    const handleKeyDown = (event: KeyboardEvent) => {
      if (!root.contains(document.activeElement)) return;
      if (event.key === 'Escape') {
        if (escapeRef.current) {
          event.preventDefault();
          escapeRef.current();
        }
        return;
      }
      if (event.key !== 'Tab') return;
      const currentElements = focusableElements(root);
      if (currentElements.length === 0) return;
      const first = currentElements[0];
      const last = currentElements[currentElements.length - 1];
      if (event.shiftKey && document.activeElement === first) {
        event.preventDefault();
        last.focus();
      } else if (!event.shiftKey && document.activeElement === last) {
        event.preventDefault();
        first.focus();
      }
    };

    document.addEventListener('keydown', handleKeyDown);
    return () => {
      document.removeEventListener('keydown', handleKeyDown);
      const target = restoreFocusRef?.current ?? restoreRef.current;
      if (target && document.contains(target)) target.focus();
    };
  }, [active, restoreFocusRef]);

  return rootRef;
}
