import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router';
import { AppShell } from '../../src/app/layout/AppShell';

describe('AppShell', () => {
  it('renders hero navigation, stats, and nested content', () => {
    render(
      <MemoryRouter initialEntries={['/modules/platform']}>
        <AppShell
          authPanel={<div>auth panel</div>}
          highlightedModules={<div>highlighted modules</div>}
          navigation={[
            {
              description: 'Live module state',
              label: 'Platform Console',
              to: '/modules/platform',
            },
          ]}
          stats={<div>shell stats</div>}
        >
          <div>shell body</div>
        </AppShell>
      </MemoryRouter>,
    );

    expect(screen.getByText('Operationally honest UI for a modular monolith.')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Platform Console' })).toHaveAttribute('href', '/modules/platform');
    expect(screen.getByText('auth panel')).toBeInTheDocument();
    expect(screen.getByText('highlighted modules')).toBeInTheDocument();
    expect(screen.getByText('shell stats')).toBeInTheDocument();
    expect(screen.getByText('shell body')).toBeInTheDocument();
  });
});