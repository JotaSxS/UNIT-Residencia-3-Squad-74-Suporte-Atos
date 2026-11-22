import { PAPEIS, useApp, type UserForm } from './useApp';
// Administração: cadastro de funcionários internos (POST /api/usuarios).
// Nesta tela, Admin, Supervisor e Agente são cadastrados sem grupo de empresa
// e a API devolve um convite de ativação (tokenAtivacao + expiraEm), tratado na Etapa 2.
import { ContentCopy, PersonAddAlt1 } from '@mui/icons-material';
import {
  Alert,
  Box,
  Button,
  CircularProgress,
  MenuItem,
  Paper,
  Stack,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableRow,
  TextField,
  Typography,
} from '@mui/material';
import { formatDateTime, parseApiDate } from '../../dates';
import FormTextField from '../form/FormTextField';
import { Eyebrow } from '../ui';

export default function UserAdmin() {
  const {
    t,
    control,
    handleSubmit,
    isSubmitting,
    papel,
    convite,
    copyStatus,
    linkRef,
    inviteLink,
    funcionarios,
    carregandoFuncionarios,
    erroFuncionarios,
    onSubmit,
    carregarFuncionarios,
    copyInvite,
    dismissInvite,
  } = useApp();

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
                onFocus={(event) => (event.currentTarget as HTMLInputElement).select()}
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
              <Button type="button" onClick={dismissInvite}>{t('users.dismissInvite')}</Button>
              {copyStatus === 'error' && <Alert severity="warning">{t('users.copyError')}</Alert>}
            </Stack>
          </Paper>
        )}

        {/* Seção: Listagem de funcionários cadastrados (apenas Admin) */}
        <>
          <Paper variant="outlined" sx={{ p: 2.5, mt: 3 }}>
            <Stack spacing={2}>
              <Typography variant="h2" sx={{ mb: 1 }}>
                {t('users.funcionariosTitle')}
              </Typography>

              {carregandoFuncionarios ? (
                <Typography color="text.secondary">{t('users.funcionariosCarregando')}</Typography>
              ) : erroFuncionarios ? (
                <Alert severity="error">{erroFuncionarios}</Alert>
              ) : funcionarios.length === 0 ? (
                <Typography color="text.secondary">{t('users.funcionariosVazios')}</Typography>
              ) : (
                <Table size="small" aria-label="funcionarios">
                  <TableHead>
                    <TableRow>
                      <TableCell>{t('users.name')}</TableCell>
                      <TableCell>{t('users.email')}</TableCell>
                      <TableCell align="right">{t('users.role')}</TableCell>
                      <TableCell align="center">{t('users.credentialStatus')}</TableCell>
                    </TableRow>
                  </TableHead>
                  <TableBody>
                    {funcionarios.map((f) => (
                      <TableRow key={f.id} sx={{ borderBottom: '1px solid #eaeaea' }}>
                        <TableCell component="td">{f.nome}</TableCell>
                        <TableCell>{f.email}</TableCell>
                        <TableCell align="right">{t(`users.roles.${f.papel}`)}</TableCell>
                        <TableCell align="center">
                          <span style={{ textTransform: 'capitalize' }}>{t(`users.credentialStates.${f.situacao}`)}</span>
                        </TableCell>
                      </TableRow>
                    ))}
                  </TableBody>
                </Table>
              )}

              <Stack direction="row" spacing={1} alignItems="center" flexWrap="wrap" useFlexGap sx={{ mt: 2 }}>
                <Button
                  variant="outlined"
                  disabled={carregandoFuncionarios}
                  onClick={() => void carregarFuncionarios()}
                >
                  {t('users.atualizar')}
                </Button>
                {t('users.qtdeFuncionarios', { count: funcionarios.length })}
              </Stack>
            </Stack>
          </Paper>
        </>
      </Stack>
    </Box>
  );
}
