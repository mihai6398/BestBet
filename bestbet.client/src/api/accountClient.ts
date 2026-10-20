import type { AddBankrollTransactionRequest, BankrollSummary, Me, UserSummary } from '../types/domain';
import { request } from './analysisClient';

/** Signing in and out, and the signed-in user's own password. */
export const authClient = {
    /** 401 for every kind of failure, by design: the server never says whether an email has an account. */
    login: (email: string, password: string, rememberMe: boolean) =>
        request<Me>('/api/auth/login', { method: 'POST', body: JSON.stringify({ email, password, rememberMe }) }),

    logout: () => request<void>('/api/auth/logout', { method: 'POST' }),

    /** Who is signed in; a 401 means nobody is. */
    me: () => request<Me>('/api/auth/me'),

    changePassword: (currentPassword: string, newPassword: string) =>
        request<Me>('/api/auth/change-password', {
            method: 'POST',
            body: JSON.stringify({ currentPassword, newPassword }),
        }),
};

/** The signed-in user's money: worked out by the server, never stored as a single number. */
export const bankrollClient = {
    get: () => request<BankrollSummary>('/api/bankroll'),

    add: (payload: AddBankrollTransactionRequest) =>
        request<BankrollSummary>('/api/bankroll/transactions', { method: 'POST', body: JSON.stringify(payload) }),

    remove: (id: number) => request<void>(`/api/bankroll/transactions/${id}`, { method: 'DELETE' }),
};

/** The admin's account management. Everyone else gets a 403 from every one of these. */
export const adminClient = {
    users: () => request<UserSummary[]>('/api/admin/users'),

    create: (email: string, temporaryPassword: string) =>
        request<UserSummary>('/api/admin/users', { method: 'POST', body: JSON.stringify({ email, temporaryPassword }) }),

    disable: (id: string) => request<void>(`/api/admin/users/${id}/disable`, { method: 'POST' }),

    enable: (id: string) => request<void>(`/api/admin/users/${id}/enable`, { method: 'POST' }),

    resetPassword: (id: string, temporaryPassword: string) =>
        request<void>(`/api/admin/users/${id}/reset-password`, {
            method: 'POST',
            body: JSON.stringify({ temporaryPassword }),
        }),
};
