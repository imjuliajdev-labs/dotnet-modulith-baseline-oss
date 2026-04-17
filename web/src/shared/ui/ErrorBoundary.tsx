import type { ErrorInfo, PropsWithChildren } from 'react';
import { Component } from 'react';
import { reportBrowserFault } from '../telemetry/browserTelemetry';
import { StartupScreen } from '../../app/layout/StartupScreen';

type ErrorBoundaryState = {
  error: Error | null;
};

export class ErrorBoundary extends Component<PropsWithChildren, ErrorBoundaryState> {
  public state: ErrorBoundaryState = { error: null };

  public static getDerivedStateFromError(error: Error): ErrorBoundaryState {
    return { error };
  }

  public componentDidCatch(error: Error, errorInfo: ErrorInfo) {
    reportBrowserFault('route.crashed', {
      componentStack: errorInfo.componentStack,
      message: error.message,
      name: error.name,
    });
  }

  public render() {
    if (this.state.error) {
      return (
        <StartupScreen
          actionLabel="Reload shell"
          detail={this.state.error.message}
          onAction={() => window.location.reload()}
          title="The route tree crashed."
        />
      );
    }

    return this.props.children;
  }
}