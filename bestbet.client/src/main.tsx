import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { BrowserRouter, Navigate, Route, Routes } from 'react-router'
import './index.css'
import { AppShell } from './components/AppShell.tsx'
import { AuthProvider } from './components/AuthProvider.tsx'
import { LanguageProvider } from './components/LanguageProvider.tsx'
import { RequireAuth } from './components/RequireAuth.tsx'
import { AccountPage } from './pages/AccountPage.tsx'
import { AdminPage } from './pages/AdminPage.tsx'
import { BetsPage } from './pages/BetsPage.tsx'
import { LoginPage } from './pages/LoginPage.tsx'

// /login is the only page outside the guard. Everything else is behind RequireAuth, and the
// calculator itself lives in the shell (see AppShell) so it survives a visit to another page.
createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <LanguageProvider>
      <AuthProvider>
        <BrowserRouter>
          <Routes>
            <Route path="/login" element={<LoginPage />} />
            <Route
              element={
                <RequireAuth>
                  <AppShell />
                </RequireAuth>
              }
            >
              <Route index element={null} />
              <Route path="bets" element={<BetsPage />} />
              <Route path="account" element={<AccountPage />} />
              <Route
                path="admin"
                element={
                  <RequireAuth adminOnly>
                    <AdminPage />
                  </RequireAuth>
                }
              />
            </Route>
            <Route path="*" element={<Navigate to="/" replace />} />
          </Routes>
        </BrowserRouter>
      </AuthProvider>
    </LanguageProvider>
  </StrictMode>,
)
