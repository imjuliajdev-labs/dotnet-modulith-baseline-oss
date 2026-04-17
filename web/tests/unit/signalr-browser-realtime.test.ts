import { describe, expect, it, vi } from 'vitest';
import { BrowserRealtimeAdapter } from '../../src/shared/realtime/browserRealtime';
import {
  createSignalRBrowserRealtimeConnectionFactory,
  STARTER_BROWSER_REALTIME_PATH,
} from '../../src/shared/realtime/signalRBrowserRealtime';

describe('createSignalRBrowserRealtimeConnectionFactory', () => {
  it('uses the configured base url and forwards SignalR messages through the shared adapter', async () => {
    const start = vi.fn(() => Promise.resolve());
    const stop = vi.fn(() => Promise.resolve());
    const on = vi.fn((methodName: string, handler: (...args: unknown[]) => void) => {
      if (methodName === 'message') {
        handler('platform.module-state.changed', { moduleKey: 'reports', runtimeState: 'enabled' });
      }
    });
    const onclose = vi.fn();
    const createConnection = vi.fn(() => ({ start, stop, on, onclose }));

    const adapter = new BrowserRealtimeAdapter(
      createSignalRBrowserRealtimeConnectionFactory({
        baseUrl: 'https://api.example.test/',
        createConnection,
      }),
    );
    const handler = vi.fn();
    adapter.subscribe('platform.module-state.changed', handler);

    await adapter.connect();
    await adapter.disconnect();

    expect(createConnection).toHaveBeenCalledWith('https://api.example.test/realtime/browser');
    expect(start).toHaveBeenCalledTimes(1);
    expect(stop).toHaveBeenCalledTimes(1);
    expect(handler).toHaveBeenCalledWith({ moduleKey: 'reports', runtimeState: 'enabled' });
    expect(onclose).toHaveBeenCalledTimes(1);
  });

  it('uses a same-origin realtime path when the base url is empty', async () => {
    const start = vi.fn(() => Promise.resolve());
    const stop = vi.fn(() => Promise.resolve());
    const on = vi.fn();
    const onclose = vi.fn();
    const createConnection = vi.fn(() => ({ start, stop, on, onclose }));

    const adapter = new BrowserRealtimeAdapter(
      createSignalRBrowserRealtimeConnectionFactory({
        baseUrl: '',
        createConnection,
      }),
    );

    await adapter.connect();

    expect(createConnection).toHaveBeenCalledWith(STARTER_BROWSER_REALTIME_PATH);
  });
});