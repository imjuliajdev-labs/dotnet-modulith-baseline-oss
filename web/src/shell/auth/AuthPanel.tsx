import { useEffect, useState } from 'react';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { z } from 'zod';
import {
  useGetCurrentActorQuery,
  useGetPreferredTimeZoneQuery,
  useSignInMutation,
  useSignOutMutation,
  useStepUpMutation,
  useUpdatePreferredTimeZoneMutation,
} from './authApi';
import { getErrorMessage, isUnauthorizedError } from '../../shared/lib/queryErrors';

const signInSchema = z.object({
  password: z.string().min(1, 'Password is required'),
  userName: z.string().min(1, 'User name is required'),
});

type SignInFormData = z.infer<typeof signInSchema>;

const stepUpSchema = z.object({
  password: z.string().min(1, 'Password is required'),
});

type StepUpFormData = z.infer<typeof stepUpSchema>;

const timeZoneSchema = z.object({
  preferredTimeZoneId: z.string().min(1, 'Time zone is required'),
});

type TimeZoneFormData = z.infer<typeof timeZoneSchema>;

export function AuthPanel() {
  const sessionQuery = useGetCurrentActorQuery();
  const preferredTimeZoneQuery = useGetPreferredTimeZoneQuery();
  const [signIn, signInState] = useSignInMutation();
  const [signOut, signOutState] = useSignOutMutation();
  const [stepUp, stepUpState] = useStepUpMutation();
  const [updatePreferredTimeZone, updatePreferredTimeZoneState] = useUpdatePreferredTimeZoneMutation();
  const [stepUpMessage, setStepUpMessage] = useState<string | null>(null);

  const session = isUnauthorizedError(sessionQuery.error) ? null : sessionQuery.data ?? null;
  const preferredTimeZone = isUnauthorizedError(preferredTimeZoneQuery.error) ? null : preferredTimeZoneQuery.data ?? null;
  const pending = signInState.isLoading || signOutState.isLoading || stepUpState.isLoading || updatePreferredTimeZoneState.isLoading;

  const signInForm = useForm<SignInFormData>({
    defaultValues: { password: 'LocalOnly!123', userName: 'admin' },
    resolver: zodResolver(signInSchema),
  });

  const stepUpForm = useForm<StepUpFormData>({
    defaultValues: { password: '' },
    resolver: zodResolver(stepUpSchema),
  });

  const timeZoneForm = useForm<TimeZoneFormData>({
    defaultValues: { preferredTimeZoneId: Intl.DateTimeFormat().resolvedOptions().timeZone || 'Etc/UTC' },
    resolver: zodResolver(timeZoneSchema),
  });

  useEffect(() => {
    if (preferredTimeZone?.preferredTimeZoneId) {
      const current = timeZoneForm.getValues('preferredTimeZoneId');
      if (current !== preferredTimeZone.preferredTimeZoneId) {
        timeZoneForm.setValue('preferredTimeZoneId', preferredTimeZone.preferredTimeZoneId);
      }
    }
  }, [preferredTimeZone?.preferredTimeZoneId, timeZoneForm]);

  useEffect(() => {
    if (!session) {
      setStepUpMessage(null);
      stepUpForm.reset({ password: '' });
    }
  }, [session, stepUpForm]);

  async function onSignIn(data: SignInFormData) {
    await signIn(data).unwrap();
  }

  async function onSavePreferredTimeZone(data: TimeZoneFormData) {
    await updatePreferredTimeZone(data).unwrap();
  }

  async function onStepUp(data: StepUpFormData) {
    setStepUpMessage(null);
    await stepUp(data).unwrap();
    stepUpForm.reset({ password: '' });
    setStepUpMessage('Recent auth refreshed for sensitive mutations.');
  }

  return (
    <aside className="auth-panel">
      <div>
        <p className="auth-panel__meta">cookie session</p>
        <h2 className="shell-card__title">Browser auth stays server-owned.</h2>
        <p className="auth-panel__help">No tokens in local storage. The shell only carries cookies and antiforgery metadata.</p>
      </div>

      {session ? (
        <>
          <div className="message" data-tone="warning">
            Signed in as <strong>{session.displayName}</strong> with roles: {session.roles?.join(', ') ?? 'none'}.
          </div>

          <div className="auth-panel__actions">
            <span className="pill">tz {preferredTimeZone?.preferredTimeZoneId ?? timeZoneForm.getValues('preferredTimeZoneId')}</span>
            <button className="button-ghost" disabled={pending} type="button" onClick={() => {
              void signOut().unwrap();
            }}>
              Sign out
            </button>
          </div>

          <form className="auth-panel__form" onSubmit={(event) => {
            void stepUpForm.handleSubmit(onStepUp)(event);
          }}>
            <label className="field">
              <span className="field__label">Refresh recent auth with password</span>
              <input
                className="field__input"
                disabled={pending}
                type="password"
                {...stepUpForm.register('password')}
                aria-invalid={!!stepUpForm.formState.errors.password}
                aria-describedby={stepUpForm.formState.errors.password ? 'step-up-password-error' : undefined}
              />
            </label>
            {stepUpForm.formState.errors.password && (
              <span id="step-up-password-error" role="alert" className="message" data-tone="danger">{stepUpForm.formState.errors.password.message}</span>
            )}

            {stepUpState.error ? <p className="message" data-tone="danger">{getErrorMessage(stepUpState.error)}</p> : null}
            {stepUpMessage ? <p className="message" data-tone="healthy">{stepUpMessage}</p> : null}

            <button className="button-ghost" disabled={pending} type="submit">
              Refresh recent auth
            </button>
          </form>

          <form className="auth-panel__form" onSubmit={(event) => {
            void timeZoneForm.handleSubmit(onSavePreferredTimeZone)(event);
          }}>
            <label className="field">
              <span className="field__label">Preferred IANA time zone</span>
              <input
                className="field__input"
                disabled={pending}
                {...timeZoneForm.register('preferredTimeZoneId')}
                aria-invalid={!!timeZoneForm.formState.errors.preferredTimeZoneId}
                aria-describedby={timeZoneForm.formState.errors.preferredTimeZoneId ? 'tz-error' : undefined}
              />
            </label>
            {timeZoneForm.formState.errors.preferredTimeZoneId && (
              <span id="tz-error" role="alert" className="message" data-tone="danger">{timeZoneForm.formState.errors.preferredTimeZoneId.message}</span>
            )}

            {updatePreferredTimeZoneState.error ? <p className="message" data-tone="danger">{getErrorMessage(updatePreferredTimeZoneState.error)}</p> : null}

            <button className="button" disabled={pending} type="submit">
              Save preferred time zone
            </button>
          </form>
        </>
      ) : (
        <form className="auth-panel__form" onSubmit={(event) => {
          void signInForm.handleSubmit(onSignIn)(event);
        }}>
          <label className="field">
            <span className="field__label">User name</span>
            <input
              className="field__input"
              {...signInForm.register('userName')}
              aria-invalid={!!signInForm.formState.errors.userName}
              aria-describedby={signInForm.formState.errors.userName ? 'username-error' : undefined}
            />
          </label>
          {signInForm.formState.errors.userName && (
            <span id="username-error" role="alert" className="message" data-tone="danger">{signInForm.formState.errors.userName.message}</span>
          )}

          <label className="field">
            <span className="field__label">Password</span>
            <input
              className="field__input"
              type="password"
              {...signInForm.register('password')}
              aria-invalid={!!signInForm.formState.errors.password}
              aria-describedby={signInForm.formState.errors.password ? 'password-error' : undefined}
            />
          </label>
          {signInForm.formState.errors.password && (
            <span id="password-error" role="alert" className="message" data-tone="danger">{signInForm.formState.errors.password.message}</span>
          )}

          {signInState.error ? <p className="message" data-tone="danger">{getErrorMessage(signInState.error)}</p> : null}

          <button className="button" disabled={pending} type="submit">
            Sign in with cookie auth
          </button>
        </form>
      )}
    </aside>
  );
}
