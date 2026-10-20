import { useState } from 'react';
import type { FormEvent } from 'react';
import { useNavigate } from 'react-router';
import { authClient } from '../api/accountClient';
import { ApiError } from '../api/analysisClient';
import { useAuth, useMe } from '../lib/auth';
import { useT } from '../lib/i18n';

/** Changing your own password — and the only page a user on a temporary password can reach. */
export function AccountPage() {
    const me = useMe();
    const { setMe } = useAuth();
    const t = useT();
    const navigate = useNavigate();

    const [current, setCurrent] = useState('');
    const [next, setNext] = useState('');
    const [confirm, setConfirm] = useState('');
    const [error, setError] = useState<string | null>(null);
    const [saved, setSaved] = useState(false);
    const [busy, setBusy] = useState(false);

    const forced = me.mustChangePassword;

    const submit = async (e: FormEvent) => {
        e.preventDefault();
        setSaved(false);
        if (next !== confirm) {
            setError(t.account.mismatch);
            return;
        }

        setBusy(true);
        setError(null);
        try {
            const updated = await authClient.changePassword(current, next);
            setCurrent('');
            setNext('');
            setConfirm('');
            setSaved(true);
            setMe(updated);
            if (forced) navigate('/', { replace: true });
        } catch (failure) {
            setError(failure instanceof ApiError ? failure.message : t.common.failed);
        } finally {
            setBusy(false);
        }
    };

    return (
        <main className="page">
            <header className="page-head">
                <h1>{t.account.title}</h1>
                <p className="tagline">{t.account.signedInAs(me.email)}</p>
            </header>

            <section className="card page-narrow">
                {forced && <p className="warning">{t.account.forced}</p>}

                <form className="auth-form" onSubmit={submit}>
                    <label className="field">
                        <span>{t.account.current}</span>
                        <input
                            type="password"
                            autoComplete="current-password"
                            required
                            value={current}
                            onChange={(e) => setCurrent(e.target.value)}
                        />
                    </label>
                    <label className="field">
                        <span>{t.account.next}</span>
                        <input
                            type="password"
                            autoComplete="new-password"
                            required
                            minLength={12}
                            value={next}
                            onChange={(e) => setNext(e.target.value)}
                        />
                    </label>
                    <label className="field">
                        <span>{t.account.confirm}</span>
                        <input
                            type="password"
                            autoComplete="new-password"
                            required
                            minLength={12}
                            value={confirm}
                            onChange={(e) => setConfirm(e.target.value)}
                        />
                    </label>
                    <p className="hint">{t.account.rule}</p>

                    {error && (
                        <p className="error" role="alert">
                            {error}
                        </p>
                    )}
                    {saved && <p className="note">{t.account.saved}</p>}

                    <button type="submit" className="primary" disabled={busy}>
                        {busy ? t.account.submitting : t.account.submit}
                    </button>
                </form>
            </section>
        </main>
    );
}
