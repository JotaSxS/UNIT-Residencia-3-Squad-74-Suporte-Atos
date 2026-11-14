import { createTheme } from '@mui/material/styles';
import type { ThemeMode } from './types';
import { RADIUS, TOKENS, TYPOGRAPHY, type AtosTheme } from './tokens';

declare module '@mui/material/styles' {
  interface Theme { atos: AtosTheme }
  interface ThemeOptions { atos?: AtosTheme }
}

export function buildTheme(mode: ThemeMode) {
  const t = TOKENS[mode];
  return createTheme({
    palette: {
      mode,
      // primary.main = vinho legível como texto/ícone no tema atual (claro: vinho escuro; escuro: vinho claro).
      // O preenchimento dos botões vem de brandPrimary (ver MuiButton.containedPrimary).
      primary: { main: t.brandText, dark: t.brandPrimaryHover, contrastText: t.primaryContrastText },
      secondary: { main: t.brandPrimaryStrong },
      error: { main: t.error },
      warning: { main: t.warning },
      info: { main: t.info },
      success: { main: t.success },
      background: { default: t.surfaceCanvas, paper: t.surfaceBase },
      text: { primary: t.textPrimary, secondary: t.textSecondary, disabled: t.textMuted },
      divider: t.borderSubtle,
    },
    shape: { borderRadius: RADIUS },
    typography: { ...TYPOGRAPHY, caption: { ...TYPOGRAPHY.caption, color: t.textMuted } },
    // Tokens extras do design system que não existem na paleta do MUI.
    atos: {
      ...t,
      tones: {
        info: { fg: t.info, bg: t.infoBg },
        warning: { fg: t.warning, bg: t.warningBg },
        error: { fg: t.error, bg: t.errorBg },
        success: { fg: t.success, bg: t.successBg },
        purple: { fg: t.purple, bg: t.purpleBg },
        muted: { fg: t.textMuted, bg: t.borderSubtle },
      },
    },
    components: {
      MuiCssBaseline: {
        styleOverrides: {
          '*::-webkit-scrollbar': { width: 10, height: 10 },
          '*::-webkit-scrollbar-thumb': { background: t.scrollbarThumb, borderRadius: 8 },
          '*::-webkit-scrollbar-track': { background: 'transparent' },
          // Foco por teclado sempre visível, nos dois temas.
          '*:focus-visible': { outline: `2px solid ${t.brandText}`, outlineOffset: 2 },
          '.MuiOutlinedInput-input:focus-visible': { outline: 'none' },
          '::selection': { background: t.brandSoft },
          // Respeita "reduzir movimento" do sistema operacional (WCAG 2.3.3 / boas práticas).
          '@media (prefers-reduced-motion: reduce)': {
            '*, *::before, *::after': { animationDuration: '0.01ms !important', animationIterationCount: '1 !important', transitionDuration: '0.01ms !important', scrollBehavior: 'auto !important' },
          },
        },
      },
      // Placeholder com contraste suficiente (o padrão do MUI usa opacidade 0.42).
      MuiInputBase: { styleOverrides: { input: { '&::placeholder': { color: t.textMuted, opacity: 1 } } } },
      MuiButton: {
        defaultProps: { disableElevation: true },
        styleOverrides: {
          root: { minHeight: 40, paddingInline: 16 },
          containedPrimary: {
            color: t.onBrand,
            background: `linear-gradient(100deg, ${t.brandPrimary}, ${t.brandPrimaryEnd})`,
            boxShadow: `0 6px 18px ${t.brandPrimaryShadow}`,
            transition: 'transform .16s ease, box-shadow .16s ease, background .16s ease',
            '&:hover': { background: t.brandPrimaryHover, '@media (prefers-reduced-motion: no-preference)': { transform: 'translateY(-1px)' }, boxShadow: `0 10px 24px ${t.brandPrimaryShadow}` },
            '&.Mui-disabled': { opacity: 0.7, color: t.onBrand },
          },
          outlined: {
            borderColor: t.borderStrong, color: t.textPrimary,
            '&:hover': { borderColor: t.brandText, background: t.brandSoft },
          },
          text: { '&:hover': { background: t.brandSoft } },
        },
      },
      MuiIconButton: { styleOverrides: { root: { '&:hover': { color: t.brandText, background: t.brandSoft } } } },
      MuiPaper: { styleOverrides: { root: { backgroundImage: 'none' }, outlined: { borderColor: t.borderSubtle } } },
      MuiOutlinedInput: {
        styleOverrides: {
          root: {
            background: t.surfaceAlt, fontSize: 13,
            '& fieldset': { borderColor: t.borderStrong },
            '&:hover fieldset': { borderColor: `${t.textSecondary} !important` },
            '&.Mui-focused fieldset': { borderColor: `${t.brandText} !important`, borderWidth: 2 },
            '&.Mui-focused': { boxShadow: `0 0 0 3px ${t.brandSoft}` },
          },
        },
      },
      MuiInputLabel: { styleOverrides: { root: { fontSize: 13 } } },
      // Campos obrigatórios: asterisco vermelho no rótulo (padrão do projeto).
      MuiFormLabel: { styleOverrides: { asterisk: { color: t.error, '&.Mui-error': { color: t.error } } } },
      MuiChip: { styleOverrides: { root: { fontWeight: 600, fontSize: 12 }, outlined: { borderColor: t.borderStrong } } },
      MuiTableCell: { styleOverrides: { root: { borderColor: t.borderSubtle, fontSize: 13 } } },
      MuiTab: { styleOverrides: { root: { minHeight: 40, fontSize: 13 } } },
      MuiTabs: { styleOverrides: { indicator: { height: 2 } } },
      MuiTooltip: { defaultProps: { arrow: true } },
      MuiDialog: { styleOverrides: { paper: { borderRadius: 14 } } },
      MuiBackdrop: { styleOverrides: { root: { backgroundColor: t.surfaceOverlay } } },
    },
  });
}
