// Campo de texto/seleção ligado ao react-hook-form. O erro vem do Yup e aparece em <LineErrorForm/> sob o campo.
// Para campos obrigatórios passe `required`: o asterisco vermelho fica no rótulo desde o início
// (a validação não é o único aviso de obrigatoriedade).
import React from 'react';
import { Box, FormLabel, TextField } from '@mui/material';
import type { TextFieldProps } from '@mui/material';
import { Controller } from 'react-hook-form';
import type { Control, FieldValues, Path } from 'react-hook-form';
import LineErrorForm from './LineErrorForm';

type BaseProps = Omit<TextFieldProps, 'name' | 'value' | 'defaultValue' | 'onChange' | 'onBlur' | 'error' | 'helperText' | 'inputRef'>;

type FormTextFieldProps<T extends FieldValues> = BaseProps & {
  name: Path<T>;
  control: Control<T>;
  /** Variáveis de interpolação da mensagem de erro (ex.: { max: 200 }). */
  errorParams?: Record<string, unknown>;
  /** Chamado depois que o valor do formulário é atualizado (útil para campos dependentes). */
  onValueChange?: (value: string) => void;
  /** Rótulo fixo acima do campo (em vez do rótulo flutuante do MUI); o asterisco vermelho de `required` continua. */
  topLabel?: boolean;
};

export default function FormTextField<T extends FieldValues>({
  name, control, errorParams, onValueChange, topLabel, ...textFieldProps
}: FormTextFieldProps<T>) {
  const errorId = `${String(name)}-error`;
  const inputId = textFieldProps.id ?? `field-${String(name)}`;
  const { label, ...withoutLabel } = textFieldProps;
  return (
    <Controller
      name={name}
      control={control}
      render={({ field, fieldState }) => (
        <Box>
          {topLabel && label && (
            <FormLabel htmlFor={inputId} required={textFieldProps.required} error={Boolean(fieldState.error)}
              sx={{ display: 'block', mb: 0.75, fontSize: 13, fontWeight: 700, color: 'text.secondary' }}>
              {label}
            </FormLabel>
          )}
          <TextField
            {...(topLabel ? withoutLabel : textFieldProps)}
            id={inputId}
            name={field.name}
            value={field.value ?? ''}
            onChange={(e) => { field.onChange(e); onValueChange?.(e.target.value); }}
            onBlur={field.onBlur}
            inputRef={field.ref}
            error={Boolean(fieldState.error)}
            inputProps={{ ...textFieldProps.inputProps, 'aria-describedby': fieldState.error ? errorId : undefined }}
          />
          <LineErrorForm id={errorId} error={fieldState.error} params={errorParams} />
        </Box>
      )}
    />
  );
}
