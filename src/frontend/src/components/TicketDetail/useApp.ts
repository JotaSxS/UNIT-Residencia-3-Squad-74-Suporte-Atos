import { yupResolver } from '@hookform/resolvers/yup';
import { useTheme } from '@mui/material/styles';
import { useState } from 'react';
import { useForm } from 'react-hook-form';
import { useTranslation } from 'react-i18next';
import { toast } from 'sonner';
import * as yup from 'yup';
import { AGENTES, STAGES } from '../../data';
import type { Message as MessageData, Role, Session, StatusKey, Ticket } from '../../types';

const messageSchema = yup.object({ text: yup.string().trim().required('validation.required') });
export type MessageFormValues = yup.InferType<typeof messageSchema>;

export interface TicketDetailProps {
  ticket: Ticket;
  role: Role;
  user: Session;
  onBack: () => void;
  onUpdate: (ticket: Ticket) => void;
}

export function useApp({ ticket, role, user, onUpdate }: TicketDetailProps) {
  const { atos } = useTheme();
  const { t } = useTranslation();
  const [tab, setTab] = useState('conversa');
  const isTeam = role !== 'cliente'; // agente e administrador atendem o chamado
  const isMine = ticket.agente === user.nome;
  const team = Array.from(new Set([user.nome, ...AGENTES])).filter((a) => a !== ticket.agente);
  const idx = STAGES.indexOf(ticket.status === 'aguardando_cliente' ? 'em_andamento' : ticket.status);
  const activeStep = ticket.status === 'fechado' ? STAGES.length : idx;

  const send = (texto: string) => {
    const msg: MessageData = {
      autor: isTeam ? (ticket.agente || t('detail.teamFallback')) : user.nome,
      tipo: isTeam ? 'agente' : 'cliente',
      data: new Date().toISOString(), texto,
    };
    onUpdate({ ...ticket, mensagens: [...ticket.mensagens, msg], status: isTeam ? 'aguardando_cliente' : 'em_andamento' });
    toast.success(isTeam ? t('detail.toasts.replyTeam') : t('detail.toasts.replyClient'));
  };

  const addNote = (texto: string) => {
    onUpdate({ ...ticket, notas: [...ticket.notas, { autor: ticket.agente || t('detail.agentFallback'), data: new Date().toISOString(), texto }] });
    toast.success(t('detail.toasts.noteSaved'));
  };

  const setStatus = (s: StatusKey) => { onUpdate({ ...ticket, status: s }); toast.success(t('detail.toasts.statusUpdated', { status: t(`status.${s}`) })); };
  const assign = (a: string | null) => {
    onUpdate({ ...ticket, agente: a, status: ticket.status === 'aberto' ? 'em_andamento' : ticket.status });
    if (!a) toast.warning(t('detail.toasts.unassigned'));
    else if (a === user.nome) toast.success(t('detail.toasts.taken'));
    else toast.success(t('detail.toasts.reassigned', { agent: a }));
  };

  return { atos, t, tab, setTab, isTeam, isMine, team, activeStep, send, addNote, setStatus, assign };
}

export function useMessageForm(onSubmit: (text: string) => void) {
  const { control, handleSubmit, reset } = useForm<MessageFormValues>({
    resolver: yupResolver(messageSchema),
    mode: 'onTouched',
    defaultValues: { text: '' },
  });
  const submit = ({ text }: MessageFormValues) => { onSubmit(text); reset(); };

  return { control, handleSubmit, submit };
}
