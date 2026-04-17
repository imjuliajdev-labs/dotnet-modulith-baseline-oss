import { describe, expect, it, vi } from 'vitest';
import { BrowserRealtimeAdapter, type BrowserRealtimeHandlers } from '../../src/shared/realtime/browserRealtime';

describe('BrowserRealtimeAdapter', () => {
  it('connects only once until disconnected', async () => {
    const start = vi.fn(() => Promise.resolve());
    const close = vi.fn();
    const factory = vi.fn(() => ({ start, close }));
    const adapter = new BrowserRealtimeAdapter(factory);

    await adapter.connect();
    await adapter.connect();

    expect(factory).toHaveBeenCalledTimes(1);
    expect(start).toHaveBeenCalledTimes(1);
    expect(adapter.isConnected).toBe(true);
  });

  it('delivers messages only to matching channel subscribers', async () => {
    let handlers: BrowserRealtimeHandlers | undefined;
    const adapter = new BrowserRealtimeAdapter((nextHandlers) => {
      handlers = nextHandlers;
      return { start: vi.fn(() => Promise.resolve()), close: vi.fn() };
    });

    const invoicesHandler = vi.fn();
    const auditHandler = vi.fn();
    adapter.subscribe('invoices.updated', invoicesHandler);
    adapter.subscribe('audit.events', auditHandler);
    await adapter.connect();

    expect(handlers).toBeDefined();
    handlers!.onMessage({ channel: 'invoices.updated', payload: { id: 'inv-1' } });

    expect(invoicesHandler).toHaveBeenCalledWith({ id: 'inv-1' });
    expect(auditHandler).not.toHaveBeenCalled();
  });

  it('stops delivering after unsubscribe and resets state when transport closes', async () => {
    const start = vi.fn(() => Promise.resolve());
    const close = vi.fn();
    let handlers: BrowserRealtimeHandlers | undefined;
    const adapter = new BrowserRealtimeAdapter((nextHandlers) => {
      handlers = nextHandlers;
      return { start, close };
    });

    const handler = vi.fn();
    const unsubscribe = adapter.subscribe('platform.modules', handler);
    await adapter.connect();

    unsubscribe();
    expect(handlers).toBeDefined();
    handlers!.onMessage({ channel: 'platform.modules', payload: { key: 'reports' } });
    handlers!.onClose();

    expect(handler).not.toHaveBeenCalled();
    expect(adapter.isConnected).toBe(false);

    await adapter.disconnect();
    expect(close).not.toHaveBeenCalled();
  });

  it('closes the transport on explicit disconnect', async () => {
    const start = vi.fn(() => Promise.resolve());
    const close = vi.fn();
    const adapter = new BrowserRealtimeAdapter(() => ({ start, close }));

    await adapter.connect();
    await adapter.disconnect();

    expect(close).toHaveBeenCalledTimes(1);
    expect(adapter.isConnected).toBe(false);
  });

  it('resets connection state when startup fails', async () => {
    const start = vi.fn(() => Promise.reject(new Error('connect failed')));
    const close = vi.fn();
    const adapter = new BrowserRealtimeAdapter(() => ({ start, close }));

    await expect(adapter.connect()).rejects.toThrow('connect failed');

    expect(close).toHaveBeenCalledTimes(1);
    expect(adapter.isConnected).toBe(false);
  });
});