export type BrowserRealtimeEnvelope<TPayload = unknown> = {
  channel: string;
  payload: TPayload;
};

export type BrowserRealtimeConnection = {
  start(): Promise<void> | void;
  close(): Promise<void> | void;
  send?(envelope: BrowserRealtimeEnvelope): Promise<void> | void;
};

export type BrowserRealtimeHandlers = {
  onClose: () => void;
  onMessage: (envelope: BrowserRealtimeEnvelope) => void;
};

export type BrowserRealtimeConnectionFactory = (handlers: BrowserRealtimeHandlers) => BrowserRealtimeConnection;

type SubscriptionHandler<TPayload> = (payload: TPayload) => void;

export class BrowserRealtimeAdapter {
  private readonly connectionFactory: BrowserRealtimeConnectionFactory;
  private connection: BrowserRealtimeConnection | null = null;
  private connectOperation: Promise<void> | null = null;
  private readonly subscriptions = new Map<string, Set<SubscriptionHandler<unknown>>>();

  public constructor(connectionFactory: BrowserRealtimeConnectionFactory) {
    this.connectionFactory = connectionFactory;
  }

  public async connect(): Promise<void> {
    if (this.connection) {
      if (this.connectOperation) {
        await this.connectOperation;
      }

      return;
    }

    const connection = this.connectionFactory({
      onClose: () => {
        this.connection = null;
        this.connectOperation = null;
      },
      onMessage: (envelope) => {
        const handlers = this.subscriptions.get(envelope.channel);
        if (!handlers) {
          return;
        }

        for (const handler of handlers) {
          handler(envelope.payload);
        }
      }
    });

    this.connection = connection;
    this.connectOperation = Promise.resolve(connection.start()).catch((error: unknown) => {
      if (this.connection === connection) {
        this.connection = null;
        this.connectOperation = null;
      }

      void Promise.resolve(connection.close()).catch(() => undefined);
      throw error;
    });

    await this.connectOperation;
  }

  public async disconnect(): Promise<void> {
    if (!this.connection) {
      return;
    }

    const current = this.connection;
    this.connection = null;
    this.connectOperation = null;
    await current.close();
  }

  public subscribe<TPayload>(channel: string, handler: SubscriptionHandler<TPayload>): () => void {
    const normalizedChannel = channel.trim();
    if (!normalizedChannel) {
      throw new Error('Realtime channel is required.');
    }

    const normalizedHandler = handler as SubscriptionHandler<unknown>;
    const handlers = this.subscriptions.get(normalizedChannel) ?? new Set<SubscriptionHandler<unknown>>();
    handlers.add(normalizedHandler);
    this.subscriptions.set(normalizedChannel, handlers);

    return () => {
      const currentHandlers = this.subscriptions.get(normalizedChannel);
      if (!currentHandlers) {
        return;
      }

      currentHandlers.delete(normalizedHandler);
      if (currentHandlers.size === 0) {
        this.subscriptions.delete(normalizedChannel);
      }
    };
  }

  public get isConnected(): boolean {
    return this.connection !== null;
  }
}