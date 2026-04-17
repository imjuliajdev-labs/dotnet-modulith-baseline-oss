import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { AuthPanel } from '../../src/shell/auth/AuthPanel';
import type {
  useGetCurrentActorQuery,
  useGetPreferredTimeZoneQuery,
  useSignInMutation,
  useSignOutMutation,
  useStepUpMutation,
  useUpdatePreferredTimeZoneMutation,
} from '../../src/shell/auth/authApi';
import type { IdentityActorSessionResponse, PreferredTimeZoneResponse } from '../../src/shared/api/base/contracts';

type CurrentActorQueryResult = ReturnType<typeof useGetCurrentActorQuery>;
type PreferredTimeZoneQueryResult = ReturnType<typeof useGetPreferredTimeZoneQuery>;
type SignInMutationResult = ReturnType<typeof useSignInMutation>;
type SignOutMutationResult = ReturnType<typeof useSignOutMutation>;
type StepUpMutationResult = ReturnType<typeof useStepUpMutation>;
type UpdatePreferredTimeZoneMutationResult = ReturnType<typeof useUpdatePreferredTimeZoneMutation>;

const useGetCurrentActorQueryMock = vi.fn<() => CurrentActorQueryResult>();
const useGetPreferredTimeZoneQueryMock = vi.fn<() => PreferredTimeZoneQueryResult>();
const useSignInMutationMock = vi.fn<() => SignInMutationResult>();
const useSignOutMutationMock = vi.fn<() => SignOutMutationResult>();
const useStepUpMutationMock = vi.fn<() => StepUpMutationResult>();
const useUpdatePreferredTimeZoneMutationMock = vi.fn<() => UpdatePreferredTimeZoneMutationResult>();

function createQueryResult<TData>(data: TData) {
  return {
    currentData: data,
    data,
    error: undefined,
    isError: false,
    isFetching: false,
    isLoading: false,
    isSuccess: true,
    isUninitialized: false,
    refetch: vi.fn(),
    status: 'fulfilled',
  };
}

function createMutationHookResult<TMutationResult>(mutation: ReturnType<typeof vi.fn>): TMutationResult {
  return [
    mutation,
    {
      error: undefined,
      isError: false,
      isLoading: false,
      isSuccess: false,
      isUninitialized: true,
      reset: vi.fn(),
      status: 'uninitialized',
    },
  ] as TMutationResult;
}

vi.mock('../../src/shell/auth/authApi', () => ({
  useGetCurrentActorQuery: () => useGetCurrentActorQueryMock(),
  useGetPreferredTimeZoneQuery: () => useGetPreferredTimeZoneQueryMock(),
  useSignInMutation: () => useSignInMutationMock(),
  useSignOutMutation: () => useSignOutMutationMock(),
  useStepUpMutation: () => useStepUpMutationMock(),
  useUpdatePreferredTimeZoneMutation: () => useUpdatePreferredTimeZoneMutationMock(),
}));

describe('AuthPanel', () => {
  beforeEach(() => {
    useGetCurrentActorQueryMock.mockReset();
    useGetPreferredTimeZoneQueryMock.mockReset();
    useSignInMutationMock.mockReset();
    useSignOutMutationMock.mockReset();
    useStepUpMutationMock.mockReset();
    useUpdatePreferredTimeZoneMutationMock.mockReset();

    const session: IdentityActorSessionResponse = {
      actorId: 'identity:seeded-admin',
      displayName: 'Baseline Admin',
      roles: ['Admin'],
      userName: 'admin',
    };

    const preferredTimeZone: PreferredTimeZoneResponse = {
      preferredTimeZoneId: 'Etc/UTC',
    };

    useGetCurrentActorQueryMock.mockReturnValue(createQueryResult(session) as CurrentActorQueryResult);
    useGetPreferredTimeZoneQueryMock.mockReturnValue(createQueryResult(preferredTimeZone) as PreferredTimeZoneQueryResult);
    useSignInMutationMock.mockReturnValue(createMutationHookResult<SignInMutationResult>(vi.fn()));
    useSignOutMutationMock.mockReturnValue(createMutationHookResult<SignOutMutationResult>(vi.fn()));
    useStepUpMutationMock.mockReturnValue(createMutationHookResult<StepUpMutationResult>(vi.fn()));
    useUpdatePreferredTimeZoneMutationMock.mockReturnValue(createMutationHookResult<UpdatePreferredTimeZoneMutationResult>(vi.fn()));
  });

  it('flips aria-invalid and shows a zod error message when step-up is submitted with an empty password', async () => {
    const stepUpMutation = vi.fn(() => ({
      unwrap: vi.fn().mockResolvedValue({ actorId: 'identity:seeded-admin' }),
    }));
    useStepUpMutationMock.mockReturnValue(createMutationHookResult<StepUpMutationResult>(stepUpMutation));

    render(<AuthPanel />);

    const passwordInput = screen.getByLabelText('Refresh recent auth with password');
    expect(passwordInput).toHaveAttribute('aria-invalid', 'false');

    fireEvent.click(screen.getByRole('button', { name: 'Refresh recent auth' }));

    await waitFor(() => {
      expect(passwordInput).toHaveAttribute('aria-invalid', 'true');
    });

    const errorMessage = await screen.findByRole('alert');
    expect(errorMessage).toHaveTextContent('Password is required');
    expect(passwordInput).toHaveAttribute('aria-describedby', errorMessage.id);
    expect(stepUpMutation).not.toHaveBeenCalled();
  });

  it('does not invoke the sign-in mutation when the zod schema rejects a blank user name', async () => {
    const signInMutation = vi.fn(() => ({
      unwrap: vi.fn().mockResolvedValue({ actorId: 'identity:seeded-admin' }),
    }));
    useSignInMutationMock.mockReturnValue(createMutationHookResult<SignInMutationResult>(signInMutation));
    useGetCurrentActorQueryMock.mockReturnValue({
      ...createQueryResult(undefined),
      data: undefined,
      currentData: undefined,
      error: { status: 401, data: undefined },
      isError: true,
      isSuccess: false,
      status: 'rejected',
    } as unknown as CurrentActorQueryResult);

    render(<AuthPanel />);

    const userNameInput = screen.getByLabelText('User name');
    const passwordInput = screen.getByLabelText('Password');
    expect(userNameInput).toHaveAttribute('aria-invalid', 'false');

    fireEvent.change(userNameInput, { target: { value: '' } });
    fireEvent.change(passwordInput, { target: { value: '' } });
    fireEvent.click(screen.getByRole('button', { name: 'Sign in with cookie auth' }));

    await waitFor(() => {
      expect(userNameInput).toHaveAttribute('aria-invalid', 'true');
    });

    expect(passwordInput).toHaveAttribute('aria-invalid', 'true');
    expect(await screen.findByText('User name is required')).toBeInTheDocument();
    expect(screen.getByText('Password is required')).toBeInTheDocument();
    expect(signInMutation).not.toHaveBeenCalled();
  });

  it('refreshes recent authentication through the step-up form', async () => {
    const stepUpMutation = vi.fn(() => ({
      unwrap: vi.fn().mockResolvedValue({ actorId: 'identity:seeded-admin' }),
    }));

    useStepUpMutationMock.mockReturnValue(createMutationHookResult<StepUpMutationResult>(stepUpMutation));

    render(<AuthPanel />);

    fireEvent.change(screen.getByLabelText('Refresh recent auth with password'), {
      target: { value: 'LocalOnly!123' },
    });

    fireEvent.click(screen.getByRole('button', { name: 'Refresh recent auth' }));

    await waitFor(() => {
      expect(stepUpMutation).toHaveBeenCalledWith({ password: 'LocalOnly!123' });
    });

    expect(await screen.findByText('Recent auth refreshed for sensitive mutations.')).toBeInTheDocument();
  });
});
