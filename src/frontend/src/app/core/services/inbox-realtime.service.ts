import { Injectable, signal } from '@angular/core';
import {
  HubConnection,
  HubConnectionBuilder,
  HubConnectionState,
  LogLevel,
} from '@microsoft/signalr';
import {
  ConversationReadState,
  MessageAddedNotification,
  RealtimeConnectionState,
} from '../models/inbox.models';

/** Callbacks the store registers once, before the connection is started. */
export interface InboxRealtimeHandlers {
  readonly onMessageAdded: (notification: MessageAddedNotification) => void;
  readonly onConversationReadStateChanged: (readState: ConversationReadState) => void;
  /**
   * Called after every successful (re)connection. Any change committed while the socket was down
   * was never pushed, so the store refetches instead of assuming it is still in sync.
   */
  readonly onConnectionEstablished: () => void;
}

/** Longest wait between reconnection attempts; the client keeps trying indefinitely. */
const maximumReconnectDelayInMilliseconds = 30_000;

@Injectable({ providedIn: 'root' })
export class InboxRealtimeService {
  private hubConnection: HubConnection | null = null;

  readonly connectionState = signal<RealtimeConnectionState>('disconnected');

  async start(handlers: InboxRealtimeHandlers): Promise<void> {
    if (this.hubConnection !== null) {
      return;
    }

    const hubConnection = new HubConnectionBuilder()
      .withUrl('/hubs/inbox')
      .withAutomaticReconnect({
        // Exponential back-off that never gives up: an inbox left open overnight must recover by
        // itself when the API is restarted.
        nextRetryDelayInMilliseconds: (retryContext) =>
          Math.min(
            maximumReconnectDelayInMilliseconds,
            1000 * 2 ** retryContext.previousRetryCount,
          ),
      })
      .configureLogging(LogLevel.Warning)
      .build();

    hubConnection.on('MessageAdded', handlers.onMessageAdded);
    hubConnection.on('ConversationReadStateChanged', handlers.onConversationReadStateChanged);

    hubConnection.onreconnecting(() => this.connectionState.set('reconnecting'));
    hubConnection.onreconnected(() => {
      this.connectionState.set('connected');
      handlers.onConnectionEstablished();
    });
    hubConnection.onclose(() => this.connectionState.set('disconnected'));

    this.hubConnection = hubConnection;
    this.connectionState.set('connecting');

    try {
      await hubConnection.start();
      this.connectionState.set('connected');
      handlers.onConnectionEstablished();
    } catch {
      // The initial attempt is not covered by the automatic reconnect policy, so retry it here.
      this.connectionState.set('disconnected');
      this.scheduleInitialConnectionRetry(handlers);
    }
  }

  async stop(): Promise<void> {
    const hubConnection = this.hubConnection;
    this.hubConnection = null;

    if (hubConnection !== null && hubConnection.state !== HubConnectionState.Disconnected) {
      await hubConnection.stop();
    }

    this.connectionState.set('disconnected');
  }

  private scheduleInitialConnectionRetry(handlers: InboxRealtimeHandlers): void {
    setTimeout(() => {
      const hubConnection = this.hubConnection;
      if (hubConnection === null || hubConnection.state !== HubConnectionState.Disconnected) {
        return;
      }

      this.connectionState.set('connecting');
      hubConnection
        .start()
        .then(() => {
          this.connectionState.set('connected');
          handlers.onConnectionEstablished();
        })
        .catch(() => {
          this.connectionState.set('disconnected');
          this.scheduleInitialConnectionRetry(handlers);
        });
    }, 5000);
  }
}
