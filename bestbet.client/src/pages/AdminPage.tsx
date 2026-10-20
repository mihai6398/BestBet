import { useCallback, useEffect, useState } from 'react';
import type { FormEvent } from 'react';
import { adminClient } from '../api/accountClient';
import { ApiError } from '../api/analysisClient';
import { ConfirmDialog } from '../components/Modal';
import { useMe } from '../lib/auth';
import { dateTime } from '../lib/format';
import { useLocale, useT } from '../lib/i18n';
import { temporaryPassword } from '../lib/password';
import type { UserSummary } from '../types/domain';

type Pending = { kind: 'disable' | 'reset'; user: UserSummary } | null;

/**
 * The admin's account list. There is no public sign-up, so this is the only way anyone gets in.
 *
 * Passwords handed out here are temporary and shown exactly once, right after they are set: the
 * server never sends a password back, and the user has to replace it before anything else works.
 */
export function AdminPage() {
    const t = useT();
    const locale = useLocale();
    const me = useMe();

    const [users, setUsers] = useState<UserSummary[]>([]);
    const [email, setEmail] = useState('');
    const [password, setPassword] = useState(() => temporaryPassword());
    const [message, setMessage] = useState<string | null>(null);
    const [error, setError] = useState<string | null>(null);
    const [busy, setBusy] = useState(false);
    const [pending, setPending] = useState<Pending>(null);

    const [version, setVersion] = useState(0);
    const reload = useCallback(() => setVersion((v) => v + 1), []);

    useEffect(() => {
        let cancelled = false;
        void (async () => {
            try {
                const rows = await adminClient.users();
                if (!cancelled) setUsers(rows);
            } catch (e) {
                if (!cancelled) setError(e instanceof ApiError ? e.message : t.common.failed);
            }
        })();
        return () => {
            cancelled = true;
        };
    }, [version, t]);

    const attempt = async (action: () => Promise<void>) => {
        setBusy(true);
        setError(null);
        setMessage(null);
        try {
            await action();
            reload();
        } catch (e) {
            setError(e instanceof ApiError ? e.message : t.common.failed);
        } finally {
            setBusy(false);
            setPending(null);
        }
    };

    const create = (e: FormEvent) => {
        e.preventDefault();
        const address = email.trim();
        const temporary = password;
        void attempt(async () => {
            await adminClient.create(address, temporary);
            setMessage(t.admin.created(address, temporary));
            setEmail('');
            setPassword(temporaryPassword());
        });
    };

    const confirmPending = () => {
        if (!pending) return;
        const { kind, user } = pending;
        if (kind === 'disable') {
            void attempt(() => adminClient.disable(user.id));
        } else {
            const temporary = temporaryPassword();
            void attempt(async () => {
                await adminClient.resetPassword(user.id, temporary);
                setMessage(t.admin.resetDone(user.email, temporary));
            });
        }
    };

    const state = (user: UserSummary) =>
        user.isDisabled
            ? t.admin.stateDisabled
            : user.isLockedOut
              ? t.admin.stateLocked
              : user.mustChangePassword
                ? t.admin.stateMustChange
                : t.admin.stateActive;

    return (
        <main className="page">
            <header className="page-head">
                <h1>{t.admin.title}</h1>
                <p className="tagline">{t.admin.intro}</p>
            </header>

            <section className="card">
                <h2>{t.admin.create}</h2>
                <form className="inline-form" onSubmit={create}>
                    <label className="field field-grow">
                        <span>{t.admin.email}</span>
                        <input type="email" required value={email} onChange={(e) => setEmail(e.target.value)} />
                    </label>
                    <label className="field field-grow">
                        <span>{t.admin.tempPassword}</span>
                        <input required minLength={12} value={password} onChange={(e) => setPassword(e.target.value)} />
                    </label>
                    <button type="button" onClick={() => setPassword(temporaryPassword())}>
                        {t.admin.generate}
                    </button>
                    <button type="submit" className="primary" disabled={busy}>
                        {t.admin.createSubmit}
                    </button>
                </form>
                {message && (
                    <p className="note secret" role="status">
                        {message}
                    </p>
                )}
                {error && (
                    <p className="error" role="alert">
                        {error}
                    </p>
                )}
            </section>

            <section className="card">
                <div className="table-scroll">
                    <table>
                        <thead>
                            <tr>
                                <th>{t.admin.columns.email}</th>
                                <th>{t.admin.columns.role}</th>
                                <th>{t.admin.columns.state}</th>
                                <th>{t.admin.columns.created}</th>
                                <th>{t.admin.columns.actions}</th>
                            </tr>
                        </thead>
                        <tbody>
                            {users.map((user) => {
                                const self = user.email === me.email;
                                return (
                                    <tr key={user.id}>
                                        <td>
                                            {user.email}
                                            {self && <span className="hint"> ({t.admin.you})</span>}
                                        </td>
                                        <td>{user.isAdmin ? t.admin.roleAdmin : t.admin.roleUser}</td>
                                        <td>{state(user)}</td>
                                        <td className="nowrap">{dateTime(user.createdUtc, locale)}</td>
                                        <td className="row-actions">
                                            {!self &&
                                                (user.isDisabled ? (
                                                    <button type="button" disabled={busy} onClick={() => void attempt(() => adminClient.enable(user.id))}>
                                                        {t.admin.enable}
                                                    </button>
                                                ) : (
                                                    <button type="button" disabled={busy} onClick={() => setPending({ kind: 'disable', user })}>
                                                        {t.admin.disable}
                                                    </button>
                                                ))}
                                            {!self && (
                                                <button type="button" disabled={busy} onClick={() => setPending({ kind: 'reset', user })}>
                                                    {t.admin.reset}
                                                </button>
                                            )}
                                        </td>
                                    </tr>
                                );
                            })}
                        </tbody>
                    </table>
                </div>
            </section>

            {pending && (
                <ConfirmDialog
                    title={pending.kind === 'disable' ? t.admin.disable : t.admin.reset}
                    message={pending.kind === 'disable' ? t.admin.confirmDisable(pending.user.email) : t.admin.confirmReset(pending.user.email)}
                    confirmLabel={pending.kind === 'disable' ? t.admin.disable : t.admin.reset}
                    danger={pending.kind === 'disable'}
                    busy={busy}
                    onConfirm={confirmPending}
                    onCancel={() => setPending(null)}
                />
            )}
        </main>
    );
}
