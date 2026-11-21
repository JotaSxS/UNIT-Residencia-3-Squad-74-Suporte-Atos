// Página pública de ativação: captura o token do convite, remove-o da URL e define a primeira senha.
// Este componente mantém o token apenas em memória, sem gravá-lo em Storage, cookies ou console.
// Links legados por query podem constar dos logs do servidor antes de a SPA carregar.
import React, { useEffect, useState } from 'react';
import {
  Alert, Box, Button, CircularProgress, IconButton, InputAdornment, Link, Paper, Stack, Typography,
} from '@mui/material';
import { useTheme } from '@mui/material/styles';
import { useTranslation } from 'react-i18next';
import { useForm } from 'react-hook-form';
import { yupResolver } from '@hookform/resolvers/yup';
import * as yup from 'yup';
import { useLocation, useNavigate } from 'react-router-dom';
import { ArrowForward, LockReset, Visibility, VisibilityOff } from '@mui/icons-material';
import { ativarFuncionario } from '../auth';
import { ApiError } from '../http';
import FormTextField from './form/FormTextField';
import LanguageSwitcher from './LanguageSwitcher';
import { BrandMark, Eyebrow } from './ui';

const activationSchema = yup.object({
  senha: yup.string().required('validation.required')
    .min(15, 'validation.passwordMin').max(128, 'validation.passwordMax'),
  confirmacao: yup.string().required('validation.required')
    .oneOf([yup.ref('senha')], 'validation.passwordMismatch'),
});
type ActivationForm = yup.InferType<typeof activationSchema>;

export default function Activate() {
  const { t } = useTranslation();
  const { atos } = useTheme();
  const location = useLocation();
  const navigate = useNavigate();
  const [token, setToken] = useState(() => (new URLSearchParams(location.hash.slice(1)).get('token') ?? new URLSearchParams(location.search).get('token'))?.trim() ?? '');
  const [submitError, setSubmitError] = useState<string | null>(null);
  const [showSenha, setShowSenha] = useState(false);
  const [showConfirmacao, setShowConfirmacao] = useState(false);

  // Captura o token antes de limpar a barra de endereços; ele permanece apenas no estado em memória.
  useEffect(() => {
    if (location.search || location.hash) navigate({ pathname: location.pathname, search: '', hash: '' }, { replace: true });
  }, [location.pathname, location.search, location.hash, navigate]);

  const { control, handleSubmit, reset, formState: { isSubmitting } } = useForm<ActivationForm>({
    resolver: yupResolver(activationSchema),
    mode: 'onTouched',
    defaultValues: { senha: '', confirmacao: '' },
  });

  const onSubmit = async ({ senha }: ActivationForm) => {
    if (!token) return;
    setSubmitError(null);
    try {
      await ativarFuncionario({ token, senha });
      // Não faz login automático: limpa os dados sensíveis e devolve o funcionário ao login.
      reset();
      setToken('');
      navigate('/login', { replace: true, state: { activationSuccess: true } });
    } catch (error) {
      // 400 cobre token inválido, expirado ou já utilizado; não repassa detalhes do servidor.
      setSubmitError(error instanceof ApiError && error.status === 400
        ? t('activation.invalidToken')
        : t('api.generic'));
    }
  };

  const fieldSx = {
    '& .MuiOutlinedInput-root': { borderRadius: '14px', bgcolor: atos.surfaceAlt },
    '& .MuiOutlinedInput-input': { py: '16px', px: '18px', fontSize: 15 },
  };

  return (
    <Box sx={{
      minHeight: '100vh', '@supports (min-height: 100dvh)': { minHeight: '100dvh' },
      display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'center',
      px: { xs: 1.5, sm: 3 }, pt: { xs: 12, sm: 10 }, pb: 5, bgcolor: 'background.default', position: 'relative',
    }}>
      <Stack direction="row" alignItems="center" justifyContent="space-between" spacing={1} flexWrap="wrap" useFlexGap
        sx={{ position: 'absolute', top: 16, left: 16, right: 16 }}>
        <BrandMark width={110} />
        <LanguageSwitcher />
      </Stack>

      <Box component="main" sx={{ width: 'min(100%, 620px)' }}>
        <Paper elevation={0} sx={{ borderRadius: { xs: '28px', sm: '40px' }, p: { xs: 3, sm: 6 }, border: 1, borderColor: 'divider', boxShadow: '0 30px 80px rgba(0,0,0,0.08)' }}>
          <Stack direction="row" alignItems="center" spacing={{ xs: 2, sm: 3 }} sx={{ mb: 3.5 }}>
            <Box aria-hidden sx={{
              width: 62, height: 62, flexShrink: 0, borderRadius: '50%', display: 'grid', placeItems: 'center',
              bgcolor: atos.brandPrimary, color: atos.onBrand,
            }}>
              <LockReset sx={{ fontSize: 28 }} />
            </Box>
            <Box sx={{ minWidth: 0 }}>
              <Eyebrow sx={{ letterSpacing: '0.2em', mb: 0.25 }}>{t('activation.eyebrow')}</Eyebrow>
              <Typography component="h1" sx={{ fontSize: 'clamp(28px, 5vw, 38px)', fontWeight: 700, lineHeight: 1.1, letterSpacing: '-0.02em' }}>
                {t('activation.title')}
              </Typography>
              <Typography color="text.secondary" sx={{ mt: 0.75, fontSize: { xs: 14, sm: 15 } }}>{t('activation.subtitle')}</Typography>
            </Box>
          </Stack>

          {!token ? (
            <Stack spacing={2}>
              <Alert severity="warning">{t('activation.missingToken')}</Alert>
              <Button type="button" variant="contained" onClick={() => navigate('/login', { replace: true })}>
                {t('activation.goToLogin')}
              </Button>
            </Stack>
          ) : (
            <Box component="form" onSubmit={handleSubmit(onSubmit)} noValidate>
              <Stack spacing={2.5}>
                {submitError && <Alert severity="error" role="alert">{submitError}</Alert>}
                <FormTextField<ActivationForm> name="senha" control={control} topLabel required fullWidth
                  autoComplete="new-password" type={showSenha ? 'text' : 'password'}
                  label={t('activation.password')} errorParams={{ min: 15, max: 128 }} sx={fieldSx}
                  InputProps={{
                    endAdornment: (
                      <InputAdornment position="end">
                        <IconButton aria-label={showSenha ? t('activation.hidePassword') : t('activation.showPassword')}
                          onClick={() => setShowSenha((v) => !v)} edge="end">
                          {showSenha ? <VisibilityOff fontSize="small" /> : <Visibility fontSize="small" />}
                        </IconButton>
                      </InputAdornment>
                    ),
                  }} />
                <FormTextField<ActivationForm> name="confirmacao" control={control} topLabel required fullWidth
                  autoComplete="new-password" type={showConfirmacao ? 'text' : 'password'}
                  label={t('activation.confirmPassword')} sx={fieldSx}
                  InputProps={{
                    endAdornment: (
                      <InputAdornment position="end">
                        <IconButton aria-label={showConfirmacao ? t('activation.hidePassword') : t('activation.showPassword')}
                          onClick={() => setShowConfirmacao((v) => !v)} edge="end">
                          {showConfirmacao ? <VisibilityOff fontSize="small" /> : <Visibility fontSize="small" />}
                        </IconButton>
                      </InputAdornment>
                    ),
                  }} />
                <Typography variant="caption" sx={{ mt: '-8px !important' }}>{t('activation.passwordHint')}</Typography>
                <Button type="submit" variant="contained" fullWidth disabled={isSubmitting} aria-busy={isSubmitting}
                  sx={{ minHeight: 56, borderRadius: '14px', fontSize: 16, fontWeight: 700 }}>
                  {isSubmitting ? <CircularProgress size={20} color="inherit" /> : <LockReset />}
                  <Box component="span" sx={{ mx: 1 }}>{isSubmitting ? t('activation.submitting') : t('activation.submit')}</Box>
                  {!isSubmitting && <ArrowForward />}
                </Button>
              </Stack>
            </Box>
          )}
        </Paper>

        <Stack direction="row" alignItems="center" justifyContent="center" spacing={1} sx={{ mt: 3.5, color: 'text.secondary' }}>
          <Typography variant="body2" sx={{ color: 'inherit' }}>{t('activation.secure')}</Typography>
          <Link component="button" type="button" underline="hover" onClick={() => navigate('/login', { replace: true })}
            sx={{ fontWeight: 700, fontSize: 14 }}>
            {t('activation.goToLogin')}
          </Link>
        </Stack>
      </Box>
    </Box>
  );
}
