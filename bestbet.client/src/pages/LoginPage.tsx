import { useState } from 'react';
import type { FormEvent } from 'react';
import { Navigate, useLocation } from 'react-router';
import { ApiError } from '../api/analysisClient';
import { LanguageSwitch } from '../components/LanguageSwitch';
import { useAuth } from '../lib/auth';
import { useT } from '../lib/i18n';

/**
 * Email and password, and nothing that would say which of the two was wrong.
 *
 * The server answers every failure with the same 401 so it never confirms that an address has an
 * account, and this page keeps to that: one message for a wrong password, an unknown email and a
 * locked account alike. Only "too many attempts" is told apart, because it is about the address the
 * request came from, not about any account.
 */
export function LoginPage() {
    const { state, login } = useAuth();
    const t = useT();
    const location = useLocation();
    const from = (location.state as { from?: string } | null)?.from ?? '/';

    const [email, setEmail] = useState('');
    const [password, setPassword] = useState('');
    const [rememberMe, setRememberMe] = useState(false);
    const [error, setError] = useState<string | null>(null);
    const [busy, setBusy] = useState(false);

    if (state.status === 'signedIn') {
        return <Navigate to={state.me.mustChangePassword ? '/account' : from} replace />;
    }

    const submit = async (e: FormEvent) => {
        e.preventDefault();
        setBusy(true);
        setError(null);
        try {
            await login(email.trim(), password, rememberMe);
        } catch (failure) {
            const status = failure instanceof ApiError ? failure.status : -1;
            setError(status === 429 ? t.auth.tooMany : status === 0 ? t.auth.unreachable : t.auth.failed);
            setPassword('');
        } finally {
            setBusy(false);
        }
    };

    return (
        <main className="auth-page">
            <section className="card auth-card">
                <div className="auth-head">
                    <h1>{t.auth.title}</h1>
                    <LanguageSwitch />
                </div>
                <p className="note">{t.auth.closed}</p>

                <form className="auth-form" onSubmit={submit}>
                    <label className="field">
                        <span>{t.auth.email}</span>
                        <input
                            type="email"
                            autoComplete="username"
                            required
                            autoFocus
                            value={email}
                            onChange={(e) => setEmail(e.target.value)}
                        />
                    </label>
                    <label className="field">
                        <span>{t.auth.password}</span>
                        <input
                            type="password"
                            autoComplete="current-password"
                            required
                            value={password}
                            onChange={(e) => setPassword(e.target.value)}
                        />
                    </label>
                    <label className="toggle">
                        <input type="checkbox" checked={rememberMe} onChange={(e) => setRememberMe(e.target.checked)} />
                        {t.auth.rememberMe}
                    </label>

                    {error && (
                        <p className="error" role="alert">
                            {error}
                        </p>
                    )}

                    <button type="submit" className="primary" disabled={busy}>
                        {busy ? t.auth.submitting : t.auth.submit}
                    </button>
                </form>
            </section>
        </main>
    );
}
