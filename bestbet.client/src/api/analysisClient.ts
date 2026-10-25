import type {
    AnalyzeRequest,
    AnalyzeResponse,
    ImportBetRequest,
    LiveFeedState,
    LiveRepriceRequest,
    LiveRepriceResult,
    Bet,
    BetStats,
    PreMatchRequest,
    PreMatchResponse,
    PreMatchStatus,
    PredictionMarket,
    ProviderReading,
    RepriceRequest,
    RepricedBet,
    SaveBetRequest,
    SettleBetRequest,
    BetStatus,
    UpdateBetRequest,
} from '../types/domain';

/**
 * Carries the server's own explanation of what was wrong with the inputs, so the form can show it
 * instead of a generic failure.
 */
export class ApiError extends Error {
    // A plain field rather than a parameter property: the project builds with erasableSyntaxOnly,
    // which rules out any TypeScript syntax that emits runtime code.
    readonly status: number;

    constructor(message: string, status: number) {
        super(message);
        this.name = 'ApiError';
        this.status = status;
    }
}

/**
 * What the request wrapper announces when the server says the session is gone (401) or that this
 * user may not do something yet (403 — most often a temporary password still to be replaced). The
 * auth provider listens and sends the user to the right page; nothing else has to handle either.
 */
export const AUTH_EVENTS = {
    unauthorized: 'bestbet:unauthorized',
    forbidden: 'bestbet:forbidden',
} as const;

/**
 * Every call to the API goes through here.
 *
 * The session is an HttpOnly cookie the browser attaches on its own — there is no token in this
 * code, or anywhere script could read one. Every request also carries the app's own header, which
 * the server requires on anything that changes state: a page on another site cannot add a custom
 * header without a CORS preflight the server would refuse.
 */
export async function request<T>(path: string, init?: RequestInit): Promise<T> {
    let response: Response;
    try {
        response = await fetch(path, {
            ...init,
            credentials: 'same-origin',
            headers: { 'Content-Type': 'application/json', 'X-BestBet-Csrf': '1', ...init?.headers },
        });
    } catch {
        throw new ApiError('Could not reach the API. Is the ASP.NET backend running?', 0);
    }

    if (response.status === 401) {
        window.dispatchEvent(new Event(AUTH_EVENTS.unauthorized));
    } else if (response.status === 403) {
        window.dispatchEvent(new Event(AUTH_EVENTS.forbidden));
    }

    if (!response.ok) {
        throw new ApiError(await readProblemDetail(response), response.status);
    }

    return response.status === 204 ? (undefined as T) : ((await response.json()) as T);
}

/** ASP.NET returns ProblemDetails on a 400; its `detail` is the message worth showing. */
async function readProblemDetail(response: Response): Promise<string> {
    try {
        const body = await response.json();
        return body?.detail ?? body?.title ?? `Request failed with ${response.status}.`;
    } catch {
        return `Request failed with ${response.status}.`;
    }
}

export const analysisClient = {
    analyze: (payload: AnalyzeRequest) =>
        request<AnalyzeResponse>('/api/analyze', { method: 'POST', body: JSON.stringify(payload) }),

    listBets: () => request<Bet[]>('/api/bets'),

    betStats: () => request<BetStats>('/api/bets/stats'),

    saveBet: (payload: SaveBetRequest) =>
        request<Bet>('/api/bets', { method: 'POST', body: JSON.stringify(payload) }),

    /** A bet already placed at a bookmaker, brought in from its slip. */
    importBet: (payload: ImportBetRequest) =>
        request<Bet>('/api/bets/import', { method: 'POST', body: JSON.stringify(payload) }),

    updateBet: (id: number, payload: UpdateBetRequest) =>
        request<Bet>(`/api/bets/${id}`, { method: 'PATCH', body: JSON.stringify(payload) }),

    deleteBet: (id: number) =>
        request<void>(`/api/bets/${id}`, { method: 'DELETE' }),

    /** One status's worth of bets — 'open', 'closed', or a single status — newest first. */
    listBetsByStatus: (status: 'open' | 'closed' | BetStatus) =>
        request<Bet[]>(`/api/bets?status=${encodeURIComponent(status)}`),

    /** Sets the result; the server works out the payout and the profit. */
    settleBet: (id: number, payload: SettleBetRequest) =>
        request<Bet>(`/api/bets/${id}/settle`, { method: 'POST', body: JSON.stringify(payload) }),

    /** What an open position is worth now. Computes; changes nothing. */
    reprice: (id: number, payload: RepriceRequest) =>
        request<RepricedBet>(`/api/bets/${id}/reprice`, {
            method: 'POST',
            body: JSON.stringify(payload),
        }),

    /** The same, against the bet's own match as the server finds and reads it now. */
    repriceLive: (id: number, payload: LiveRepriceRequest) =>
        request<LiveRepriceResult>(`/api/bets/${id}/reprice/live`, {
            method: 'POST',
            body: JSON.stringify(payload),
        }),

    /**
     * The only call in this app that leaves the machine, and it goes through our own API so the
     * search term is the single thing that travels.
     */
    searchPolymarket: (query: string) =>
        request<PredictionMarket[]>(`/api/reference/polymarket?q=${encodeURIComponent(query)}`),

    /** The last reading pushed in from a match page, plus the token the pushing page needs. */
    liveFeed: () => request<LiveFeedState>('/api/live-feed'),

    clearLiveFeed: () => request<void>('/api/live-feed', { method: 'DELETE' }),

    /**
     * One reading of a match the server fetches itself, from a link or an id the user pasted.
     *
     * Unlike the push route there is nothing stored behind this: each call is a fresh look at a
     * match in progress, which is why it can be repeated on a timer without any state going stale.
     */
    providerReading: (match: string) =>
        request<ProviderReading>(`/api/live-feed/provider?match=${encodeURIComponent(match)}`),

    /** Whether the pre-match model has a key to work with, and which competitions it can read. */
    preMatchStatus: () => request<PreMatchStatus>('/api/prematch/status'),

    /** Team form and player scoring for a fixture. Sends the competition and team names only. */
    preMatch: (payload: PreMatchRequest) =>
        request<PreMatchResponse>('/api/prematch', { method: 'POST', body: JSON.stringify(payload) }),
};
