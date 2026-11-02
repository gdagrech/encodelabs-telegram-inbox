/**
 * Typed mirrors of the API read models. The backend serialises these in camel case, both over HTTP
 * and over SignalR, so one set of interfaces covers both channels.
 */

export type MessageDirection = 'Incoming' | 'Outgoing';

export interface ContactSummary {
  readonly contactId: string;
  readonly displayName: string;
  readonly username: string | null;
  readonly unreadMessageCount: number;
  readonly lastMessagePreview: string | null;
  readonly lastMessageDirection: MessageDirection | null;
  readonly lastActivityAtUtc: string;
}

export interface MessageView {
  readonly messageId: string;
  /** Server-assigned arrival order; also the cut-off used when marking a conversation as read. */
  readonly sequenceNumber: number;
  readonly direction: MessageDirection;
  readonly text: string;
  readonly sentAtUtc: string;
  readonly isRead: boolean;
}

export interface ConversationHistory {
  readonly contactId: string;
  readonly displayName: string;
  readonly username: string | null;
  readonly messages: readonly MessageView[];
  readonly highestSequenceNumber: number;
}

export interface ConversationReadState {
  readonly contactId: string;
  readonly unreadMessageCount: number;
  readonly readUpToSequenceNumber: number;
}

/** Payload of the SignalR <c>MessageAdded</c> event. */
export interface MessageAddedNotification {
  readonly contactSummary: ContactSummary;
  readonly message: MessageView;
}

export type RealtimeConnectionState = 'connecting' | 'connected' | 'reconnecting' | 'disconnected';
