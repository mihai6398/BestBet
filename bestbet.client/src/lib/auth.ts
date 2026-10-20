import { createContext, useContext } from 'react';
import type { Me } from '../types/domain';

/**
 * Who is using the app, as far as the browser knows.
 *
 * 'checking' lasts only until the first /api/auth/me answers. 'unreachable' is kept apart from
 * 'anonymous' on purpose: a backend that is down is not a reason to show a login form that cannot
 * work, and pretending otherwise sends people hunting for a password problem they do not have.
 */
export type AuthState =
    | { status: 'checking' }
    | { status: 'unreachable' }
    | { status: 'anonymous' }
    | { status: 'signedIn'; me: Me };

export interface AuthContextValue {
    state: AuthState;
    /** Resolves with the signed-in user; rejects with the ApiError the server sent. */
    login: (email: string, password: string, rememberMe: boolean) => Promise<Me>;
    logout: () => Promise<void>;
    /** Asks the server again who is signed in. */
    refresh: () => Promise<void>;
    /** Replaces the user after something changed them, such as a new password. */
    setMe: (me: Me) => void;
}

export const AuthContext = createContext<AuthContextValue | null>(null);

export function useAuth(): AuthContextValue {
    const value = useContext(AuthContext);
    if (value === null) {
        throw new Error('useAuth must be used inside <AuthProvider>.');
    }

    return value;
}

/** The signed-in user. Only for components that render behind <RequireAuth>, where there always is one. */
export function useMe(): Me {
    const { state } = useAuth();
    if (state.status !== 'signedIn') {
        throw new Error('useMe must be used behind <RequireAuth>.');
    }

    return state.me;
}
