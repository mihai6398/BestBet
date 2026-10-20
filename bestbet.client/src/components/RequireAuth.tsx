import type { ReactNode } from 'react';
import { Navigate, useLocation } from 'react-router';
import { useAuth } from '../lib/auth';
import { useT } from '../lib/i18n';

interface Props {
    children: ReactNode;
    /** Only for admins; anyone else is sent to the calculator. */
    adminOnly?: boolean;
}

/**
 * The route guard: nobody past it without a session, nobody on a temporary password anywhere but the
 * page that replaces it, nobody but an admin on the admin page.
 *
 * This is for the user's sake, not the data's. The server enforces every one of these rules on
 * every request regardless; the guard only saves people from landing on a page that cannot load.
 */
export function RequireAuth({ children, adminOnly = false }: Props) {
    const { state, refresh } = useAuth();
    const t = useT();
    const location = useLocation();

    if (state.status === 'checking') {
        return <p className="page-status">{t.auth.checking}</p>;
    }

    if (state.status === 'unreachable') {
        return (
            <div className="page-status">
                <p className="error">{t.auth.unreachable}</p>
                <button type="button" onClick={() => void refresh()}>
                    {t.auth.retry}
                </button>
            </div>
        );
    }

    if (state.status === 'anonymous') {
        return <Navigate to="/login" replace state={{ from: location.pathname + location.search }} />;
    }

    if (state.me.mustChangePassword && location.pathname !== '/account') {
        return <Navigate to="/account" replace />;
    }

    if (adminOnly && !state.me.isAdmin) {
        return <Navigate to="/" replace />;
    }

    return <>{children}</>;
}
