import { ErrorBoundary } from '../shared/ui/ErrorBoundary';
import { AppRouter } from './router/AppRouter';

export function App() {
  return (
    <ErrorBoundary>
      <AppRouter />
    </ErrorBoundary>
  );
}