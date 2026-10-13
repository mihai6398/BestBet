import type {
    AnalyzeRequest,
    AnalyzeResponse,
    ImportPositionRequest,
    Position,
    PositionsSummary,
    PredictionMarket,
    RepriceRequest,
    RepricedPosition,
    SavePositionRequest,
    UpdatePositionRequest,
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

async function request<T>(path: string, init?: RequestInit): Promise<T> {
    let response: Response;
    try {
        response = await fetch(path, {
            ...init,
            headers: { 'Content-Type': 'application/json', ...init?.headers },
        });
    } catch {
        throw new ApiError('Could not reach the API. Is the ASP.NET backend running?', 0);
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

    listPositions: () => request<Position[]>('/api/positions'),

    positionsSummary: () => request<PositionsSummary>('/api/positions/summary'),

    savePosition: (payload: SavePositionRequest) =>
        request<Position>('/api/positions', { method: 'POST', body: JSON.stringify(payload) }),

    /** A bet already placed at a bookmaker, brought in from its slip. */
    importPosition: (payload: ImportPositionRequest) =>
        request<Position>('/api/positions/import', { method: 'POST', body: JSON.stringify(payload) }),

    updatePosition: (id: number, payload: UpdatePositionRequest) =>
        request<Position>(`/api/positions/${id}`, { method: 'PATCH', body: JSON.stringify(payload) }),

    deletePosition: (id: number) =>
        request<void>(`/api/positions/${id}`, { method: 'DELETE' }),

    /** What an open position is worth now. Computes; changes nothing. */
    reprice: (id: number, payload: RepriceRequest) =>
        request<RepricedPosition>(`/api/positions/${id}/reprice`, {
            method: 'POST',
            body: JSON.stringify(payload),
        }),

    /**
     * The only call in this app that leaves the machine, and it goes through our own API so the
     * search term is the single thing that travels.
     */
    searchPolymarket: (query: string) =>
        request<PredictionMarket[]>(`/api/reference/polymarket?q=${encodeURIComponent(query)}`),
};
