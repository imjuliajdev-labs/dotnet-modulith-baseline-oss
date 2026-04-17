import { render, screen } from '@testing-library/react';
import { vi } from 'vitest';
import { ErrorBoundary } from '../../src/shared/ui/ErrorBoundary';

function ThrowingRoute(): never {
  throw new Error('boom');
}

describe('ErrorBoundary', () => {
  it('renders a crash fallback when the route tree throws', () => {
    const consoleError = vi.spyOn(console, 'error').mockImplementation(() => {});

    render(
      <ErrorBoundary>
        <ThrowingRoute />
      </ErrorBoundary>,
    );

    expect(screen.getByText('The route tree crashed.')).toBeInTheDocument();
    expect(screen.getByText('boom')).toBeInTheDocument();

    consoleError.mockRestore();
  });
});