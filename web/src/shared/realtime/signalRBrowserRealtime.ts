import { HubConnectionBuilder, HttpTransportType } from '@microsoft/signalr';
import { frontendEnvironment } from '../lib/env';
import {
  type BrowserRealtimeConnection,
  type BrowserRealtimeConnectionFactory,
  type BrowserRealtimeHandlers,
} from './browserRealtime';

export const STARTER_BROWSER_REALTIME_PATH = '/realtime/browser';

type SignalRConnectionLike = {
  start(): Promise<void>;
  stop(): Promise<void>;
  on(methodName: string, newMethod: (...args: unknown[]) => void): void;
  onclose(callback: ((error?: Error) => void) | null): void;
};

type SignalRConnectionFactoryOptions = {
  baseUrl?: string;
  createConnection?: (url: string) => SignalRConnectionLike;
};

export function createSignalRBrowserRealtimeConnectionFactory(
  options: SignalRConnectionFactoryOptions = {},
): BrowserRealtimeConnectionFactory {
  const url = resolveBrowserRealtimeUrl(options.baseUrl ?? frontendEnvironment.signalRUrl);
  const createConnection = options.createConnection ?? createDefaultSignalRConnection;

  return (handlers) => createBrowserRealtimeConnection(createConnection(url), handlers);
}

function createBrowserRealtimeConnection(
  connection: SignalRConnectionLike,
  handlers: BrowserRealtimeHandlers,
): BrowserRealtimeConnection {
  connection.on('message', (channel, payload) => {
    if (typeof channel !== 'string') {
      return;
    }

    handlers.onMessage({ channel, payload });
  });

  connection.onclose(() => {
    handlers.onClose();
  });

  return {
    start: () => connection.start(),
    close: () => connection.stop(),
  };
}

function createDefaultSignalRConnection(url: string): SignalRConnectionLike {
  return new HubConnectionBuilder()
    .withUrl(url, {
      transport: HttpTransportType.WebSockets | HttpTransportType.LongPolling,
      withCredentials: true,
    })
    .withAutomaticReconnect()
    .build();
}

function resolveBrowserRealtimeUrl(baseUrl: string): string {
  const normalizedBaseUrl = baseUrl.trim().replace(/\/+$/, '');
  return normalizedBaseUrl.length === 0
    ? STARTER_BROWSER_REALTIME_PATH
    : `${normalizedBaseUrl}${STARTER_BROWSER_REALTIME_PATH}`;
}