import type { ErrorInfo, PropsWithChildren } from 'react';
import { Component } from 'react';
import { reportBrowserFault } from '../telemetry/browserTelemetry';

type FeatureErrorBoundaryProps = PropsWithChildren<{
  featureLabel: string;
}>;

type FeatureErrorBoundaryState = {
  error: Error | null;
};

export class FeatureErrorBoundary extends Component<FeatureErrorBoundaryProps, FeatureErrorBoundaryState> {
  public state: FeatureErrorBoundaryState = { error: null };

  public static getDerivedStateFromError(error: Error): FeatureErrorBoundaryState {
    return { error };
  }

  public componentDidCatch(error: Error, errorInfo: ErrorInfo) {
    reportBrowserFault('route.crashed', {
      componentStack: errorInfo.componentStack,
      feature: this.props.featureLabel,
      message: error.message,
      name: error.name,
    });
  }

  public render() {
    if (this.state.error) {
      return (
        <section className="shell-card" style={{ margin: '2rem auto', maxWidth: '40rem' }}>
          <div className="shell-card__header">
            <div>
              <h2 className="shell-card__title">{this.props.featureLabel} encountered an error</h2>
              <p className="shell-card__subcopy">{this.state.error.message}</p>
            </div>
            <span className="pill" data-tone="danger">crashed</span>
          </div>
          <div style={{ padding: '1rem' }}>
            <button
              className="button"
              onClick={() => this.setState({ error: null })}
              type="button"
            >
              Retry
            </button>
          </div>
        </section>
      );
    }

    return this.props.children;
  }
}
