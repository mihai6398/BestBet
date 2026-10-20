import { LANGUAGES, useLanguage } from '../lib/i18n';

/** The RO / EN toggle, wherever a page needs it — the login page included. */
export function LanguageSwitch() {
    const { lang, setLang, t } = useLanguage();

    return (
        <div className="lang-switch" role="group" aria-label={t.app.language}>
            {LANGUAGES.map((code) => (
                <button
                    key={code}
                    type="button"
                    className={code === lang ? 'lang-active' : undefined}
                    aria-pressed={code === lang}
                    onClick={() => setLang(code)}
                >
                    {t.langName[code]}
                </button>
            ))}
        </div>
    );
}
