import type { ToneKey } from './types';

export const TOKENS = {
  // Paleta institucional: VINHO. Vermelho puro fica reservado para erro / alerta / ações destrutivas (fechar, excluir).
  // Contrastes conferidos (WCAG): texto >= 4.5:1, bordas de campos e ícones >= 3:1, nos dois temas.
  //   brandPrimary     = preenchimento de botões e destaques (texto branco por cima)
  //   brandPrimaryEnd  = fim do degradê do botão
  //   brandPrimaryHover= botão em hover
  //   brandText        = cor de vinho usada como TEXTO/ÍCONE sobre o fundo da tela (links, abas, eyebrow, gráficos)
  //   brandSoft        = fundo suave em vinho (ícones, hover, cursor de gráfico)
  light: {
    brandPrimary: '#7a1f3f', brandPrimaryEnd: '#6b1a37', brandPrimaryHover: '#5c1530',
    brandText: '#7a1f3f', brandSoft: 'rgba(122,31,63,0.10)',
    brandPrimaryStrong: '#6b1b35', brandPrimaryMuted: '#4a1228',
    brandPrimaryShadow: 'rgba(122,31,63,0.28)', onBrand: '#ffffff', primaryContrastText: '#ffffff',
    surfaceCanvas: '#f7f5f6', surfaceSoft: '#fbf9f9', surfaceBase: '#ffffff', surfaceAlt: '#faf8f9', surfaceOverlay: 'rgba(0,0,0,0.45)',
    borderSubtle: '#e6e1e3', borderDefault: '#d6d1d3', borderStrong: '#8f8f8f',
    textPrimary: '#212121', textSecondary: '#555555', textMuted: '#6b6b6b', textInverse: '#ffffff',
    success: '#1b6b2f', successBg: 'rgba(27,107,47,0.14)',
    warning: '#9a4a00', warningBg: 'rgba(245,158,11,0.18)',
    error: '#c62828', errorBg: 'rgba(198,40,40,0.12)',
    info: '#1565c0', infoBg: 'rgba(21,101,192,0.12)',
    purple: '#5e35b1', purpleBg: 'rgba(94,53,177,0.12)',
    menuGradient: 'linear-gradient(0deg, #4a1228, #6b1b35)', menuText: '#ffffff',
    tableHeaderBg: '#efeaec', tableRowAlt: '#faf8f9', scrollbarThumb: '#c9c2c5',
  },
  dark: {
    brandPrimary: '#b03a63', brandPrimaryEnd: '#9b2c55', brandPrimaryHover: '#c24673',
    brandText: '#ec9bb2', brandSoft: 'rgba(236,155,178,0.14)',
    brandPrimaryStrong: '#6b1b35', brandPrimaryMuted: '#4a1228',
    brandPrimaryShadow: 'rgba(176,58,99,0.40)', onBrand: '#ffffff', primaryContrastText: '#2b0d19',
    surfaceCanvas: '#0f0c0d', surfaceSoft: '#141011', surfaceBase: '#1a1a1a', surfaceAlt: '#232021', surfaceOverlay: 'rgba(0,0,0,0.65)',
    borderSubtle: '#2e2a2b', borderDefault: '#4a4546', borderStrong: '#7a7577',
    textPrimary: '#f5f5f5', textSecondary: '#cfcfcf', textMuted: '#a3a3a3', textInverse: '#212121',
    success: '#66bb6a', successBg: 'rgba(102,187,106,0.20)',
    warning: '#ffa726', warningBg: 'rgba(255,167,38,0.20)',
    error: '#ff6e6b', errorBg: 'rgba(255,110,107,0.20)',
    info: '#42a5f5', infoBg: 'rgba(66,165,245,0.20)',
    purple: '#b39ddb', purpleBg: 'rgba(179,157,219,0.20)',
    menuGradient: 'linear-gradient(0deg, #2b0d19, #3d1223)', menuText: '#f5f5f5',
    tableHeaderBg: '#262223', tableRowAlt: '#1f1c1d', scrollbarThumb: '#4a4546',
  },
};

export const FONT = '"IBM Plex Sans", system-ui, sans-serif';
export const MONO = '"IBM Plex Mono", ui-monospace, monospace';

export type Tokens = (typeof TOKENS)['light'];
export interface Tone { fg: string; bg: string }
export type AtosTheme = Tokens & { tones: Record<ToneKey, Tone> };

export const RADIUS = 8;

export const TYPOGRAPHY = {
  fontFamily: FONT,
  h1: { fontSize: 24, fontWeight: 600, letterSpacing: '-0.5px' },
  h2: { fontSize: 16, fontWeight: 600 },
  h3: { fontSize: 14, fontWeight: 700 },
  body1: { fontSize: 14 },
  body2: { fontSize: 13 },
  caption: { fontSize: 12 },
  overline: { fontSize: 12, fontWeight: 700, letterSpacing: '0.14em', lineHeight: 1.6 },
  button: { textTransform: 'none', fontWeight: 600, fontSize: 13 },
} as const;
