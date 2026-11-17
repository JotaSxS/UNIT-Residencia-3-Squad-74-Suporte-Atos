// Linha de erro de formulário: exibe, logo abaixo do campo, a mensagem de validação (react-hook-form + Yup).
// A mensagem do Yup é uma chave de tradução (ex.: "validation.required"); aqui ela é traduzida na hora de exibir,
// então o erro acompanha a troca de idioma.
import React from 'react';
import { FormHelperText } from '@mui/material';
import { useTranslation } from 'react-i18next';
import type { FieldError } from 'react-hook-form';

interface LineErrorFormProps {
  error?: FieldError | string;
  /** Variáveis de interpolação da mensagem (ex.: { max: 200 }). */
  params?: Record<string, unknown>;
  id?: string;
}

export default function LineErrorForm({ error, params, id }: LineErrorFormProps) {
  const { t } = useTranslation();
  const message = typeof error === 'string' ? error : error?.message;
  if (!message) return null;
  // Erro devolvido pela API (type "server") já vem como texto pronto; os demais são chaves de tradução.
  const isServerError = typeof error !== 'string' && error?.type === 'server';
  return (
    <FormHelperText id={id} error role="alert" sx={{ mx: 0, mt: 0.5 }}>
      {isServerError ? message : t(message, params)}
    </FormHelperText>
  );
}
