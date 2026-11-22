import { useEffect, useMemo, useRef, useState, useSyncExternalStore } from 'react';
import { useTranslation } from 'react-i18next';
import { useLocation, useNavigate, useParams } from 'react-router-dom';
import { getAuthState, logout as logoutSession, restoreSession, subscribeAuth } from '../auth';
import { seed, seedNotifications } from '../data';
import type { Lang } from '../i18n';
import i18n, { LANGS } from '../i18n';
import { notifyApiError } from '../notify';
import { buildTheme } from '../theme';
import type { AppNotification, Session, ThemeMode, Ticket } from '../types';

export interface TicketDetailRouteProps {
  tickets: Ticket[];
  session: Session;
  onUpdate: (ticket: Ticket) => void;
}

export function useApp() {
  const { t: tr, i18n: i18nInstance } = useTranslation();
  const lang: Lang = (LANGS as readonly string[]).includes(i18nInstance.language) ? (i18nInstance.language as Lang) : 'pt';
  const location = useLocation();
  const navigate = useNavigate();
  const activationSuccess = Boolean((location.state as { activationSuccess?: boolean } | null)?.activationSuccess);
  const [themeMode, changeTheme] = useState<ThemeMode>(() => {
    try { return localStorage.getItem('atos.theme') === 'dark' ? 'dark' : 'light'; }
    catch { return 'light'; }
  });
  const setThemeMode = (mode: ThemeMode) => {
    changeTheme(mode);
    try { localStorage.setItem('atos.theme', mode); } catch { /* Storage may be disabled. */ }
  };
  const theme = useMemo(() => buildTheme(themeMode), [themeMode]);

  const { session, status: authStatus } = useSyncExternalStore(subscribeAuth, getAuthState);
  useEffect(() => { void restoreSession(); }, []);
  const [tickets, setTickets] = useState<Ticket[]>(seed);
  const [loadingView, setLoadingView] = useState(false);
  const [mobileNavOpen, setMobileNavOpen] = useState(false);
  const [notifications, setNotifications] = useState<AppNotification[]>(seedNotifications);
  const [showNewTicket, setShowNewTicket] = useState(false);
  const unreadCount = notifications.filter((n) => !n.lida).length;

  const markAllRead = () => setNotifications((prev) => prev.map((n) => ({ ...n, lida: true })));
  const markOneRead = (id: number) => setNotifications((prev) => prev.map((n) => (n.id === id ? { ...n, lida: true } : n)));

  const updateTicket = (updated: Ticket) => setTickets((prev) => prev.map((t) => (t.id === updated.id ? updated : t)));
  const createTicket = (t: Ticket) => {
    setTickets((prev) => [t, ...prev]);
    setNotifications((prev) => [{ id: Date.now(), texto: i18n.t('notifications.ticketRegistered', { id: t.id }), data: i18n.t('notifications.now'), lida: false }, ...prev]);
    setShowNewTicket(false);
  };
  const login = (_session: Session) => {
    if (location.pathname === '/' || location.pathname === '/login') navigate('/chamados', { replace: true });
  };
  const logout = () => {
    setShowNewTicket(false);
    navigate('/login', { replace: true });
    void logoutSession().catch(error => notifyApiError(error, tr('api.generic')));
  };

  const mainRef = useRef<HTMLElement>(null);
  // Troca de rota: fecha o menu, mostra o carregamento e leva o foco para o conteúdo (leitores de tela e teclado).
  useEffect(() => {
    if (!session) return undefined;
    setMobileNavOpen(false);
    setLoadingView(true);
    const timer = window.setTimeout(() => setLoadingView(false), 400);
    mainRef.current?.focus({ preventScroll: true });
    return () => window.clearTimeout(timer);
  }, [location.pathname, session]);

  return {
    tr,
    lang,
    location,
    navigate,
    activationSuccess,
    themeMode,
    setThemeMode,
    theme,
    session,
    authStatus,
    tickets,
    loadingView,
    mobileNavOpen,
    setMobileNavOpen,
    notifications,
    showNewTicket,
    setShowNewTicket,
    unreadCount,
    markAllRead,
    markOneRead,
    updateTicket,
    createTicket,
    login,
    logout,
    mainRef,
  };
}

export function useTicketDetailRoute({ tickets, session }: TicketDetailRouteProps) {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const ticket = tickets.find((x) => x.id === id);
  const canView = Boolean(ticket && (session.role !== 'cliente' || ticket.empresa === session.empresa));
  const backToList = () => navigate('/chamados');
  return { ticket, canView, backToList };
}
