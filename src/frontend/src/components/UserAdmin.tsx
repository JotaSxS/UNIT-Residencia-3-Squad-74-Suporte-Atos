// Administração: cadastro de funcionários internos (POST /api/usuarios).
// Nesta tela, Admin, Supervisor e Agente são cadastrados sem grupo de empresa
// e a API devolve um convite de ativação (tokenAtivacao + expiraEm), tratado na Etapa 2.
import React, { useRef, useState } from 'react';
import { Alert, Box, Button, CircularProgress, MenuItem, Paper, Stack, TextField, Typography } from '@mui/material';
import { useTranslation } from 'react-i18next';
import { useForm } from 'react-hook-form';
import { yupResolver } from '@hookform/resolvers/yup';
import * as yup from 'yup';
import { toast } from 'sonner';
import { ContentCopy, PersonAddAlt1 } from '@mui/icons-material';
import { ApiError, cadastrarUsuario, isConviteFuncionario } from '../api';
import type { ConviteFuncionarioDto, PapelInterno } from '../api';
import { formatDateTime, parseApiDate } from '../dates';
import { notifyApiError } from '../notify';
import FormTextField from './form/FormTextField';
import { Eyebrow } from './ui';

const PAPEIS: PapelInterno[] = ['Agente', 'Supervisor', 'Admin'];

const userSchema = yup.object({
  nome: yup.string().trim().required('validation.required').max(150, 'validation.maxLength'),
  email: yup.string().trim().required('validation.required').email('validation.email').max(255, 'validation.maxLength'),
  papel: yup.string().oneOf(PAPEIS, 'validation.required').required('validation.required'),
});
type UserForm = yup.InferType<typeof userSchema>;

export default function UserAdmin() {
  const { t } = useTranslation();

  const { control, handleSubmit, reset, setError, watch, formState: { isSubmitting } } = useForm<UserForm>({
    resolver: yupResolver(userSchema),
    mode: 'onTouched',
    defaultValues: { nome: '', email: '', papel: 'Agente' },
  });
  const papel = watch('papel');
  const [convite, setConvite] = useState<ConviteFuncionarioDto | null>(null);
  const [copyStatus, setCopyStatus] = useState<'idle' | 'copied' | 'error'>('idle');
  const linkRef = useRef<HTMLInputElement>(null);
  const inviteLink = convite
    ? `${window.location.origin}/ativar#token=${encodeURIComponent(convite.tokenAtivacao)}`
    : '';

  const onSubmit = async (data: UserForm) => {
    // Nao substituir o unico convite antes da confirmacao explicita do administrador.
    if (convite) return;
    setCopyStatus('idle');
    try {
      const criado = await cadastrarUsuario({
        nome: data.nome,
        email: data.email,
        grupoEmpresaId: null,
        papel: data.papel as PapelInterno,
      });
      if (!isConviteFuncionario(criado)) {
        toast.error(t('users.unexpectedInvite'));
        return;
      }
      toast.success(t('users.created', { name: criado.nome }));
      reset({ nome: '', email: '', papel: data.papel });
      setConvite(criado);
    } catch (err) {
      // 409: e-mail já cadastrado — mostra no próprio campo.
      if (err instanceof ApiError && err.status === 409) setError('email', { type: 'server', message: t('users.emailExists') });
      else notifyApiError(err, t('users.createError'));
    }
  };

  const copyInvite = async () => {
    if (!inviteLink) return;
    try {
      if (!navigator.clipboard?.writeText) throw new Error('clipboard unavailable');
      await navigator.clipboard.writeText(inviteLink);
      setCopyStatus('copied');
    } catch {
      // Mantém o campo selecionável para cópia manual quando a Clipboard API não está disponível.
      setCopyStatus('error');
      linkRef.current?.focus();
      linkRef.current?.select();
    }
  };

  return (
    <Box sx={{ maxWidth: 520 }}>
      <Box sx={{ mb: 3.5 }}>
        <Eyebrow>{t('users.eyebrow')}</Eyebrow>
        <Typography variant="h1">{t('users.title')}</Typography>
        <Typography color="text.secondary" sx={{ mt: 0.5 }}>{t('users.subtitle')}</Typography>
      </Box>

      <Stack spacing={2.5}>
        <Paper variant="outlined" component="form" onSubmit={handleSubmit(onSubmit)} noValidate sx={{ p: 2.5 }}>
          <Typography variant="h2" sx={{ mb: 2 }}>{t('users.newUser')}</Typography>
          <Stack spacing={2}>
            <FormTextField<UserForm> name="nome" control={control} required fullWidth size="small" autoComplete="off"
              label={t('users.name')} errorParams={{ max: 150 }} />
            <FormTextField<UserForm> name="email" control={control} required fullWidth size="small" type="email"
              autoComplete="off" label={t('users.email')} placeholder={t('users.emailPlaceholder')} errorParams={{ max: 255 }} />
            <FormTextField<UserForm> name="papel" control={control} select required fullWidth size="small" label={t('users.role')}>
              {PAPEIS.map((p) => <MenuItem key={p} value={p}>{t(`users.roles.${p}`)}</MenuItem>)}
            </FormTextField>
            {papel && <Typography variant="caption" sx={{ mt: '-8px !important' }}>{t(`users.roleHints.${papel}`)}</Typography>}
            <Typography variant="body2" color="text.secondary">{t('users.inviteHint')}</Typography>
            <Button type="submit" variant="contained" disabled={isSubmitting || Boolean(convite)}
              startIcon={isSubmitting ? <CircularProgress size={14} color="inherit" /> : <PersonAddAlt1 />}>
              {isSubmitting ? t('users.submitting') : t('users.submit')}
            </Button>
          </Stack>
        </Paper>

        {convite && (
          <Paper variant="outlined" sx={{ p: 2.5 }} aria-live="polite">
            <Stack spacing={1.5}>
              <Typography variant="h2">{t('users.inviteReadyTitle')}</Typography>
              <Typography variant="body2">{t('users.inviteReady', { name: convite.nome })}</Typography>
              <TextField
                inputRef={linkRef}
                value={inviteLink}
                fullWidth
                size="small"
                label={t('users.inviteLink')}
                InputProps={{ readOnly: true }}
                onFocus={(event) => event.currentTarget.select()}
              />
              <Typography variant="caption">
                {t('users.inviteExpiresAt', { date: formatDateTime(parseApiDate(convite.expiraEm)) })}
              </Typography>
              <Typography variant="body2" color="text.secondary">{t('users.inviteSingleUse')}</Typography>
              <Stack direction="row" spacing={1} alignItems="center" flexWrap="wrap" useFlexGap>
                <Button type="button" variant="outlined" startIcon={<ContentCopy />} onClick={copyInvite}>
                  {t('users.copyLink')}
                </Button>
                {copyStatus === 'copied' && (
                  <Typography role="status" variant="body2" color="success.main">{t('users.copied')}</Typography>
                )}
              </Stack>
              <Button type="button" onClick={() => { setConvite(null); setCopyStatus('idle'); }}>{t('users.dismissInvite')}</Button>
              {copyStatus === 'error' && <Alert severity="warning">{t('users.copyError')}</Alert>}
            </Stack>
          </Paper>
        )}
      </Stack>
    </Box>
  );
}
