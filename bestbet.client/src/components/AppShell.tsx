import { NavLink, Outlet, useLocation, useNavigate } from 'react-router';
import App from '../App';
import { useAuth, useMe } from '../lib/auth';
import { useT } from '../lib/i18n';
import { LanguageSwitch } from './LanguageSwitch';

/**
 * Everything behind the login: the bar with the pages and the signed-in user, and the page itself.
 *
 * The calculator is kept mounted while another page is open, only hidden. It holds a match in
 * progress — the form, the live monitor, the result on screen — and a look at "My bets" during a
 * match must not throw all of that away. A user still on a temporary password gets no calculator at
 * all: every call it makes would be refused until the password is changed.
 */
export function AppShell() {
    const me = useMe();
    const { logout } = useAuth();
    const t = useT();
    const location = useLocation();
    const navigate = useNavigate();

    const onCalculator = location.pathname === '/';
    const ready = !me.mustChangePassword;

    const signOut = async () => {
        await logout();
        navigate('/login', { replace: true });
    };

    return (
        <>
            <nav className="shell-bar" aria-label={t.nav.main}>
                <span className="shell-brand">BestBet</span>
                {ready && (
                    <div className="shell-links">
                        <NavLink to="/" end>
                            {t.nav.calculator}
                        </NavLink>
                        <NavLink to="/bets">{t.nav.bets}</NavLink>
                        {me.isAdmin && <NavLink to="/admin">{t.nav.admin}</NavLink>}
                    </div>
                )}
                <div className="shell-user">
                    <NavLink to="/account" title={t.nav.account}>
                        {me.email}
                    </NavLink>
                    <LanguageSwitch />
                    <button type="button" className="link-button" onClick={() => void signOut()}>
                        {t.nav.logout}
                    </button>
                </div>
            </nav>

            {ready && (
                <div hidden={!onCalculator}>
                    <App active={onCalculator} />
                </div>
            )}
            {!onCalculator && <Outlet />}
        </>
    );
}
