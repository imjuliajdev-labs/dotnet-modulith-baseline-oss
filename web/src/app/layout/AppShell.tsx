import type { PropsWithChildren, ReactNode } from 'react';
import { NavLink } from 'react-router';

type NavigationItem = {
  description: string;
  label: string;
  to: string;
};

type AppShellProps = PropsWithChildren<{
  authPanel: ReactNode;
  highlightedModules: ReactNode;
  navigation: NavigationItem[];
  stats: ReactNode;
}>;

export function AppShell({ authPanel, children, highlightedModules, navigation, stats }: AppShellProps) {
  return (
    <div className="app-shell">
      <div className="app-shell__frame">
        <section className="app-shell__hero">
          <div className="app-shell__hero-grid">
            <div>
              <p className="app-shell__eyebrow">Full-stack baseline</p>
              <h1 className="app-shell__title">Operationally honest UI for a modular monolith.</h1>
              <p className="app-shell__copy">
                Generated contracts feed the shell, RTK Query owns remote data, and feature routes only appear when the Platform module says they are live.
              </p>
            </div>

            {authPanel}
          </div>

          <nav className="app-shell__nav" aria-label="Feature navigation">
            <NavLink className={({ isActive }) => isActive ? 'app-shell__nav-link app-shell__nav-link--active' : 'app-shell__nav-link'} end to="/">
              Home
            </NavLink>
            {navigation.map((item) => (
              <NavLink className={({ isActive }) => isActive ? 'app-shell__nav-link app-shell__nav-link--active' : 'app-shell__nav-link'} key={item.to} to={item.to}>
                {item.label}
              </NavLink>
            ))}
          </nav>

          <div className="hero-stat-grid">{stats}</div>
          {highlightedModules}
        </section>

        {children}
      </div>
    </div>
  );
}