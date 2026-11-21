// Tela de login única. Existem dois tipos de conta (administrador e membro da equipe); o perfil vem da conta, não de uma aba.
import React, { useState } from 'react';
import {
  Alert, Box, Button, Checkbox, CircularProgress, FormControlLabel, IconButton, InputAdornment, Link, Paper, Stack, Typography,
} from '@mui/material';
import { useTheme } from '@mui/material/styles';
import { useTranslation } from 'react-i18next';
import { useForm } from 'react-hook-form';
import { yupResolver } from '@hookform/resolvers/yup';
import * as yup from 'yup';
import { toast } from 'sonner';
import { ArrowForward, DarkModeOutlined, LightModeOutlined, VerifiedUserOutlined, Visibility, VisibilityOff } from '@mui/icons-material';
import { login } from '../auth';
import { notifyApiError } from '../notify';
import FormTextField from './form/FormTextField';
import LanguageSwitcher from './LanguageSwitcher';
import { BrandMark, Eyebrow } from './ui';
import type { Session, ThemeMode } from '../types';

const loginSchema = yup.object({
  email: yup.string().trim().required('validation.required').email('validation.email'),
  password: yup.string().required('validation.required'),
});
type LoginForm = yup.InferType<typeof loginSchema>;

interface LoginScreenProps {
  onLogin: (session: Session) => void;
  themeMode: ThemeMode;
  setThemeMode: (mode: ThemeMode) => void;
  /** Mensagem exibida depois que o funcionário conclui a ativação da credencial. */
  activationSuccess?: boolean;
}

export default function LoginScreen({ onLogin, themeMode, setThemeMode, activationSuccess = false }: LoginScreenProps) {
  const { t } = useTranslation();
  const { atos } = useTheme();
  const [loading, setLoading] = useState(false);
  const [showSenha, setShowSenha] = useState(false);

  const { control, handleSubmit } = useForm<LoginForm>({
    resolver: yupResolver(loginSchema),
    mode: 'onTouched',
    defaultValues: { email: '', password: '' },
  });

  const onSubmit = async ({ email, password }: LoginForm) => {
    setLoading(true);
    try { onLogin(await login(email, password)); }
    catch (error) { notifyApiError(error, t('api.generic')); }
    finally { setLoading(false); }
  };

  const fieldSx = {
    '& .MuiOutlinedInput-root': { borderRadius: '14px', bgcolor: atos.surfaceAlt },
    '& .MuiOutlinedInput-input': { py: '18px', px: '20px', fontSize: 16 },
  };

  return (
    <Box sx={{
      minHeight: '100vh', '@supports (min-height: 100dvh)': { minHeight: '100dvh' },
      display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'center',
      px: { xs: 1.5, sm: 3 }, pt: { xs: 12, sm: 10 }, pb: 5, bgcolor: 'background.default', position: 'relative',
    }}>
      {/* Barra superior em fluxo (flex): marca, idioma e tema nunca se sobrepõem, mesmo em 320 px. */}
      <Stack direction="row" alignItems="center" justifyContent="space-between" spacing={1} flexWrap="wrap" useFlexGap
        sx={{ position: 'absolute', top: 16, left: 16, right: 16 }}>
        <BrandMark onDark={themeMode === 'dark'} width={110} />
        <Stack direction="row" alignItems="center" spacing={1} sx={{ ml: 'auto' }}>
          <LanguageSwitcher />
          <Button size="small" variant="outlined" onClick={() => setThemeMode(themeMode === 'light' ? 'dark' : 'light')}
            startIcon={themeMode === 'light' ? <DarkModeOutlined /> : <LightModeOutlined />}
            aria-label={t('login.themeToggleAria')}
            sx={{ borderRadius: 9, minHeight: 32, fontSize: 12, bgcolor: 'background.paper' }}>
            {themeMode === 'light' ? t('login.themeDark') : t('login.themeLight')}
          </Button>
        </Stack>
      </Stack>

      <Box component="main" sx={{ width: 'min(100%, 680px)' }}>
        <Paper elevation={0} sx={{ borderRadius: { xs: '28px', sm: '40px' }, p: { xs: 3, sm: 7 }, border: 1, borderColor: 'divider', boxShadow: '0 30px 80px rgba(0,0,0,0.08)' }}>
          <Stack direction="row" alignItems="center" spacing={{ xs: 2, sm: 3 }} sx={{ mb: { xs: 3, sm: 5 } }}>
            <Box aria-hidden sx={{
              width: { xs: 56, sm: 68 }, height: { xs: 56, sm: 68 }, flexShrink: 0, borderRadius: '50%', display: 'grid', placeItems: 'center',
              bgcolor: atos.brandPrimary, color: atos.onBrand,
            }}>
              <VerifiedUserOutlined sx={{ fontSize: { xs: 26, sm: 30 } }} />
            </Box>
            <Box sx={{ minWidth: 0 }}>
              <Eyebrow sx={{ letterSpacing: '0.2em', mb: 0.25 }}>{t('login.eyebrow')}</Eyebrow>
              <Typography component="h1" sx={{ fontSize: 'clamp(30px, 6vw, 42px)', fontWeight: 700, lineHeight: 1.1, letterSpacing: '-0.02em' }}>
                {t('login.welcome')}
              </Typography>
              <Typography color="text.secondary" sx={{ mt: 0.75, fontSize: { xs: 14, sm: 16 } }}>{t('login.subtitle')}</Typography>
            </Box>
          </Stack>

          {activationSuccess && <Alert severity="success" sx={{ mb: 3 }}>{t('activation.success')}</Alert>}

          <Box component="form" onSubmit={handleSubmit(onSubmit)} noValidate>
            <Stack spacing={3}>
              <FormTextField<LoginForm> name="email" control={control} topLabel required fullWidth type="email" autoComplete="username"
                label={t('login.form.email')} placeholder={t('login.form.emailPlaceholder')} sx={fieldSx} />
              <FormTextField<LoginForm> name="password" control={control} topLabel required fullWidth autoComplete="current-password"
                type={showSenha ? 'text' : 'password'} label={t('login.form.password')} sx={fieldSx}
                InputProps={{
                  endAdornment: (
                    <InputAdornment position="end">
                      <IconButton aria-label={showSenha ? t('login.form.hidePassword') : t('login.form.showPassword')} onClick={() => setShowSenha((v) => !v)} edge="end">
                        {showSenha ? <VisibilityOff fontSize="small" /> : <Visibility fontSize="small" />}
                      </IconButton>
                    </InputAdornment>
                  ),
                }} />

              <Stack direction="row" justifyContent="space-between" alignItems="center" flexWrap="wrap" useFlexGap spacing={1}>
                <FormControlLabel control={<Checkbox size="small" checked disabled />} label={t('login.form.keepConnected')} slotProps={{ typography: { fontSize: 14, color: 'text.secondary' } }} />
                <Link component="button" type="button" underline="hover" onClick={() => toast.warning(t('login.forgotInfo'))}
                  sx={{ fontWeight: 700, fontSize: 14, color: 'primary.main' }}>
                  {t('login.form.forgotPassword')}
                </Link>
              </Stack>

              <Button type="submit" variant="contained" fullWidth disabled={loading} aria-busy={loading}
                sx={{ minHeight: 68, borderRadius: '14px', fontSize: 18, fontWeight: 700, justifyContent: 'space-between', px: 3.5 }}>
                {loading ? <CircularProgress size={22} color="inherit" /> : <VerifiedUserOutlined />}
                <span>{loading ? t('login.form.submitting') : t('login.form.submit')}</span>
                {loading ? <Box sx={{ width: 24 }} /> : <ArrowForward />}
              </Button>
            </Stack>
          </Box>
        </Paper>

        <Stack direction="row" alignItems="center" justifyContent="center" spacing={1} sx={{ mt: 3.5, color: 'text.secondary' }}>
          <VerifiedUserOutlined sx={{ fontSize: 18 }} aria-hidden />
          <Typography variant="body2" sx={{ color: 'inherit' }}>{t('login.secure')}</Typography>
        </Stack>
      </Box>
    </Box>
  );
}
