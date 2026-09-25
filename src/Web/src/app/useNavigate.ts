import { useCallback } from 'react';

export function useNavigate() {
  return useCallback((path: string) => {
    window.history.pushState({}, '', path);
    window.dispatchEvent(new PopStateEvent('popstate'));
  }, []);
}
