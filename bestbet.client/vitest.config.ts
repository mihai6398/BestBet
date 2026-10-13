import { defineConfig } from 'vitest/config';

/**
 * Separate from vite.config.ts on purpose. That file resolves an ASP.NET dev certificate for the dev
 * server, which shells out to `dotnet` — something a test run has no business needing, and something
 * a CI or container runner would not have.
 */
export default defineConfig({
    test: {
        include: ['src/**/*.test.ts'],
        // jsdom rather than node: the paste parsers use DOMParser to read the HTML flavour of a
        // clipboard, and that is browser API surface the tests have to exercise for real.
        environment: 'happy-dom',
    },
});
