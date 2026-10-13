import { useMemo } from 'react';
import type { ReactNode } from 'react';
import { useLocalStorage } from '../hooks/useLocalStorage';
import { DEFAULT_LANG, LanguageContext, dictionaryFor } from '../lib/i18n';
import type { Lang } from '../lib/i18n';

/**
 * Holds the chosen language and hands the matching copy down the tree.
 *
 * The choice is remembered in the same place the bankroll is: a user who picked Romanian once should
 * not have to pick it again at half time.
 */
export function LanguageProvider({ children }: { children: ReactNode }) {
    const [lang, setLang] = useLocalStorage<Lang>('bestbet.lang', DEFAULT_LANG);

    const value = useMemo(
        () => ({ lang, setLang, t: dictionaryFor(lang) }),
        [lang, setLang],
    );

    return <LanguageContext.Provider value={value}>{children}</LanguageContext.Provider>;
}
