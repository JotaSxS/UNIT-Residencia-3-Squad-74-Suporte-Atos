import { yupResolver } from '@hookform/resolvers/yup';
import { useCallback, useEffect, useRef, useState } from 'react';
import { useForm } from 'react-hook-form';
import { useTranslation } from 'react-i18next';
import { toast } from 'sonner';
import * as yup from 'yup';
import type { ConviteFuncionarioDto, FuncionarioListaDto, PapelInterno } from '../../api';
import { ApiError, cadastrarUsuario, isConviteFuncionario, listarFuncionarios } from '../../api';
import { notifyApiError } from '../../notify';

export const PAPEIS: PapelInterno[] = ['Agente', 'Supervisor', 'Admin'];

const userSchema = yup.object({
  nome: yup.string().trim().required('validation.required').max(150, 'validation.maxLength'),
  email: yup.string().trim().required('validation.required').email('validation.email').max(255, 'validation.maxLength'),
  papel: yup.string().oneOf(PAPEIS, 'validation.required').required('validation.required'),
});
export type UserForm = yup.InferType<typeof userSchema>;

export function useApp() {
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
  const [funcionarios, setFuncionarios] = useState<FuncionarioListaDto[]>([]);
  const [carregandoFuncionarios, setCarregandoFuncionarios] = useState(false);
  const [erroFuncionarios, setErroFuncionarios] = useState<string | null>(null);

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
      if (!mounted.current) return;
      if (!isConviteFuncionario(criado)) {
        toast.error(t('users.unexpectedInvite'));
        return;
      }
      toast.success(t('users.created', { name: criado.nome }));
      reset({ nome: '', email: '', papel: data.papel });
      setConvite(criado);
      void carregarFuncionarios();
    } catch (err) {
      if (!mounted.current) return;
      // 409: e-mail já cadastrado — mostra no próprio campo.
      if (err instanceof ApiError && err.status === 409) setError('email', { type: 'server', message: t('users.emailExists') });
      else notifyApiError(err, t('users.createError'));
    }
  };

  const mounted = useRef(false);
  const requestVersion = useRef(0);
  const carregarFuncionarios = useCallback(async () => {
    const version = ++requestVersion.current;
    if (!mounted.current) return;
    setCarregandoFuncionarios(true);
    setErroFuncionarios(null);
    try {
      const data = await listarFuncionarios();
      if (mounted.current && version === requestVersion.current) setFuncionarios(data);
    } catch {
      if (mounted.current && version === requestVersion.current) setErroFuncionarios(t('api.generic'));
    } finally {
      if (mounted.current && version === requestVersion.current) setCarregandoFuncionarios(false);
    }
  }, [t]);

  useEffect(() => {
    mounted.current = true;
    void carregarFuncionarios();
    return () => { mounted.current = false; requestVersion.current++; };
  }, [carregarFuncionarios]);

  const copyInvite = async () => {
    if (!inviteLink) return;
    try {
      if (!navigator.clipboard?.writeText) throw new Error('clipboard unavailable');
      await navigator.clipboard.writeText(inviteLink);
      if (mounted.current) setCopyStatus('copied');
    } catch {
      // Mantém o campo selecionável para cópia manual quando a Clipboard API não está disponível.
      if (!mounted.current) return;
      setCopyStatus('error');
      linkRef.current?.focus();
      linkRef.current?.select();
    }
  };

  const dismissInvite = () => { setConvite(null); setCopyStatus('idle'); };

  return {
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
  };
}
