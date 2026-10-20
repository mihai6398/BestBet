import { useCallback, useEffect, useMemo, useState } from 'react';
import type { ReactNode } from 'react';
import { authClient } from '../api/accountClient';
import { ApiError, AUTH_EVENTS } from '../api/analysisClient';
import { AuthContext } from '../lib/auth';
import type { AuthContextValue, AuthState } from '../lib/auth';
import type { Me } from '../types/domain';

/**
 * Holds who is signed in, asks the server once on load, and keeps the answer current.
 *
 * Nothing about the session is stored in the browser. The cookie that carries it is HttpOnly, so
 * this code cannot see it even if it wanted to; the only way to know is to ask /api/auth/me, and
 * the only signal that it has gone is a 401 from any call — which the request wrapper announces and
 * this turns into "signed out", so every page falls back to the login without handling it itself.
 */
export function AuthProvider({ children }: { children: ReactNode }) {
    const [state, setState] = useState<AuthState>({ status: 'checking' });

    const refresh = useCallback(async () => {
        try {
            const me = await authClient.me();
            setState({ status: 'signedIn', me });
        } catch (e) {
            setState(e instanceof ApiError && e.status === 0 ? { status: 'unreachable' } : { status: 'anonymous' });
        }
    }, []);

    useEffect(() => {
        // Synchronising with an external system — the session the server holds — is what effects are
        // for, and on first load there is no event to hang the question on instead.
        // eslint-disable-next-line react/set-state-in-effect
        void refresh();
    }, [refresh]);

    useEffect(() => {
        const signedOut = () => setState((current) => (current.status === 'anonymous' ? current : { status: 'anonymous' }));
        // A 403 is most often a temporary password still to be replaced; asking again picks up the
        // flag, and the route guard sends the user to change it.
        const forbidden = () => void refresh();

        window.addEventListener(AUTH_EVENTS.unauthorized, signedOut);
        window.addEventListener(AUTH_EVENTS.forbidden, forbidden);
        return () => {
            window.removeEventListener(AUTH_EVENTS.unauthorized, signedOut);
            window.removeEventListener(AUTH_EVENTS.forbidden, forbidden);
        };
    }, [refresh]);

    const login = useCallback(async (email: string, password: string, rememberMe: boolean) => {
        const me = await authClient.login(email, password, rememberMe);
        setState({ status: 'signedIn', me });
        return me;
    }, []);

    const logout = useCallback(async () => {
        try {
            await authClient.logout();
        } finally {
            setState({ status: 'anonymous' });
        }
    }, []);

    const setMe = useCallback((me: Me) => setState({ status: 'signedIn', me }), []);

    const value = useMemo<AuthContextValue>(
        () => ({ state, login, logout, refresh, setMe }),
        [state, login, logout, refresh, setMe],
    );

    return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}
