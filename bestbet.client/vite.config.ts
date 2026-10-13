import { fileURLToPath, URL } from 'node:url';

import { defineConfig } from 'vite';
import plugin from '@vitejs/plugin-react';
import fs from 'fs';
import path from 'path';
import child_process from 'child_process';
import { env } from 'process';

/**
 * Where the dev server sends `/api`. Visual Studio sets these when it launches the SPA proxy; the
 * fallback is the HTTPS port from the server's launchSettings.
 */
const target = env.ASPNETCORE_HTTPS_PORT
    ? `https://localhost:${env.ASPNETCORE_HTTPS_PORT}`
    : env.ASPNETCORE_URLS
      ? env.ASPNETCORE_URLS.split(';')[0]
      : 'https://localhost:7243';

/**
 * The ASP.NET dev certificate, exported on demand so the dev server can serve HTTPS on the same
 * terms as the backend.
 *
 * Only ever called for `vite dev`. It shells out to `dotnet`, which does not exist in the Node image
 * the client is built in, so calling it during `vite build` would break every containerised build.
 * Set BESTBET_DEV_HTTP=true to run the dev server over plain HTTP instead.
 */
function devCertificate() {
    if (env.BESTBET_DEV_HTTP === 'true') {
        return undefined;
    }

    const baseFolder =
        env.APPDATA !== undefined && env.APPDATA !== ''
            ? `${env.APPDATA}/ASP.NET/https`
            : `${env.HOME}/.aspnet/https`;

    const certificateName = 'bestbet.client';
    const certFilePath = path.join(baseFolder, `${certificateName}.pem`);
    const keyFilePath = path.join(baseFolder, `${certificateName}.key`);

    if (!fs.existsSync(certFilePath) || !fs.existsSync(keyFilePath)) {
        if (
            0 !==
            child_process.spawnSync(
                'dotnet',
                ['dev-certs', 'https', '--export-path', certFilePath, '--format', 'Pem', '--no-password'],
                { stdio: 'inherit' },
            ).status
        ) {
            throw new Error('Could not create certificate.');
        }
    }

    return {
        key: fs.readFileSync(keyFilePath),
        cert: fs.readFileSync(certFilePath),
    };
}

// https://vitejs.dev/config/
export default defineConfig(({ command }) => ({
    plugins: [plugin()],
    resolve: {
        alias: {
            '@': fileURLToPath(new URL('./src', import.meta.url)),
        },
    },
    server: {
        proxy: {
            '^/api': {
                target,
                secure: false,
            },
        },
        port: 5173,
        https: command === 'serve' ? devCertificate() : undefined,
    },
}));
