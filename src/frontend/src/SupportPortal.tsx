import React, { useMemo, useState } from 'react';
import { Box, CssBaseline, ThemeProvider } from '@mui/material';
import { Navigate, Route, Routes, useLocation, useNavigate, useParams } from 'react-router-dom';
import { LocalizationProvider } from '@mui/x-date-pickers/LocalizationProvider';
import { AdapterDayjs } from '@mui/x-date-pickers/AdapterDayjs';
import { enUS, esES, ptBR } from '@mui/x-date-pickers/locales';
import { useTranslation } from 'react-i18next';
import { Toaster } from 'sonner';
import { buildTheme } from './theme';
import { seed, seedNotifications } from './data';
import i18n, { DAYJS_LOCALE, LANGS } from './i18n';
import type { Lang } from './i18n';
import LoginScreen from './components/Login';
import { Sidebar, TopBar } from './components/Layout';
import TicketList, { NewTicketModal } from './components/TicketList';
import TicketDetail from './components/TicketDetail';
import Reports from './components/Reports';
import UserAdmin from './components/UserAdmin';
import type { AppNotification, Session, ThemeMode, Ticket } from './types';

// Textos dos seletores de data (x-date-pickers) por idioma da interface.
const PICKER_TEXT = {
  pt: ptBR.components.MuiLocalizationProvider.defaultProps.localeText,
  en: enUS.components.MuiLocalizationProvider.defaultProps.localeText,
  es: esES.components.MuiLocalizationProvider.defaultProps.localeText,
};

function TicketPage({ tickets, session, onUpdate }: {
  tickets: Ticket[];
  session: Session;
  onUpdate: (ticket: Ticket) => void;
}) {
  const { id } = useParams();
  const navigate = useNavigate();
  const ticket = tickets.find((item) => item.id === id);
  if (!ticket || (session.role === 'cliente' && ticket.empresa !== session.empresa)) {
    return <Navigate to="/chamados" replace />;
  }
  return <TicketDetail key={ticket.id} ticket={ticket} role={session.role} user={session}
    onBack={() => navigate('/chamados')} onUpdate={onUpdate} />;
}

export default function SupportPortal() {
  const { i18n: i18nInstance } = useTranslation();
  const lang: Lang = (LANGS as readonly string[]).includes(i18nInstance.language) ? (i18nInstance.language as Lang) : 'pt';
  const location = useLocation();
  const navigate = useNavigate();
  const [themeMode, changeTheme] = useState<ThemeMode>(() => {
    try { return localStorage.getItem('atos.theme') === 'dark' ? 'dark' : 'light'; }
    catch { return 'light'; }
  });
  const setThemeMode = (mode: ThemeMode) => {
    changeTheme(mode);
    try { localStorage.setItem('atos.theme', mode); } catch { /* Storage may be disabled. */ }
  };
  const theme = useMemo(() => buildTheme(themeMode), [themeMode]);

  const [session, setSession] = useState<Session | null>(null);
  const [tickets, setTickets] = useState<Ticket[]>(seed);
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
  const login = (user: Session) => {
    setSession(user);
    const target = location.state?.from;
    navigate(typeof target === 'string' && /^\/(chamados|relatorios|usuarios)(\/|$)/.test(target) ? target : '/chamados', { replace: true });
  };
  const logout = () => {
    setSession(null);
    setShowNewTicket(false);
    setMobileNavOpen(false);
    navigate('/login', { replace: true });
  };

  return (
    <ThemeProvider theme={theme}>
      <LocalizationProvider dateAdapter={AdapterDayjs} adapterLocale={DAYJS_LOCALE[lang]} localeText={PICKER_TEXT[lang]}>
      <CssBaseline />
      {!session ? (
        <Routes>
          <Route path="/login" element={<LoginScreen onLogin={login} themeMode={themeMode} setThemeMode={setThemeMode} />} />
          <Route path="*" element={<Navigate to="/login" replace state={{ from: location.pathname }} />} />
        </Routes>
      ) : (
        <Box sx={{ display: 'flex', height: '100vh', overflow: 'hidden', bgcolor: 'background.default' }}>
          <Sidebar
            session={session} onLogout={logout}
            themeMode={themeMode} setThemeMode={setThemeMode}
            mobileOpen={mobileNavOpen} onMobileClose={() => setMobileNavOpen(false)}
          />
          <Box sx={{ flex: 1, minWidth: 0, height: '100%', overflowY: 'auto', display: 'flex', flexDirection: 'column' }}>
            <TopBar
              session={session} themeMode={themeMode} notifications={notifications} unreadCount={unreadCount}
              onOpenNav={() => setMobileNavOpen(true)} onMarkAllRead={markAllRead} onMarkOneRead={markOneRead}
            />
            <Box component="main" sx={{ flex: 1, px: { xs: 2.5, md: 5 }, py: { xs: 3, md: 4 } }}>
              <Routes>
                <Route path="/chamados" element={<TicketList tickets={tickets} role={session.role} user={session}
                  onOpen={(id) => navigate('/chamados/' + encodeURIComponent(id))} onNew={() => setShowNewTicket(true)} />} />
                <Route path="/chamados/:id" element={<TicketPage tickets={tickets} session={session} onUpdate={updateTicket} />} />
                <Route path="/relatorios" element={<Reports tickets={tickets} role={session.role} user={session} />} />
                <Route path="/usuarios" element={session.role === 'admin' ? <UserAdmin /> : <Navigate to="/chamados" replace />} />
                <Route path="*" element={<Navigate to="/chamados" replace />} />
              </Routes>
            </Box>
          </Box>
          {showNewTicket && session.role === 'cliente' && <NewTicketModal user={session} onClose={() => setShowNewTicket(false)} onCreate={createTicket} />}
        </Box>
      )}
      {/* Notificações: sonner (toast.success / toast.error / toast.warning). */}
      <Toaster position="bottom-center" richColors closeButton theme={themeMode} />
      </LocalizationProvider>
    </ThemeProvider>
  );
}
