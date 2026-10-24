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

    // Takes an updater too, for state that several requests in flight write to at once: each has to
    // build on what the last one left, not on the value it saw when it started.
    const update = useCallback(
        (next: T | ((current: T) => T)) => {
            setValue((current) => {
                const value = typeof next === 'function' ? (next as (current: T) => T)(current) : next;
                try {
                    window.localStorage.setItem(key, JSON.stringify(value));
                } catch {
                    // Nothing to do — the value still works for this session.
                }
                return value;
            });
        },
        [key],
    );

    return [value, update] as const;
}
