import { useCallback, useState } from 'react';

/**
 * State that survives a reload, for the handful of values it would be annoying to retype.
 *
 * Wrapped in try/catch throughout: private windows and locked-down browsers throw on access rather
 * than returning null, and a bankroll field is not worth crashing the app over.
 */
export function useLocalStorage<T>(key: string, initialValue: T) {
    const [value, setValue] = useState<T>(() => {
        try {
            const stored = window.localStorage.getItem(key);
            return stored === null ? initialValue : (JSON.parse(stored) as T);
        } catch {
            return initialValue;
        }
    });

    const update = useCallback(
        (next: T) => {
            setValue(next);
            try {
                window.localStorage.setItem(key, JSON.stringify(next));
            } catch {
                // Nothing to do — the value still works for this session.
            }
        },
        [key],
    );

    return [value, update] as const;
}
